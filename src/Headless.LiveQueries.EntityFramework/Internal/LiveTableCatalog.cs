// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Maps a SQL command text, or an entity type, to the dependency keys of the tables it names. A table is found in a
/// command by the text the provider writes for its name, so the search needs no SQL parser and works for every
/// relational provider.
/// </summary>
/// <remarks>
/// The schema is part of the key and not of the search. A provider may write one schema-qualified name several
/// ways (PostgreSQL quotes each part only when it must, so <c>sales."Orders"</c> and <c>"sales"."Orders"</c> are the
/// same table), and hand-written SQL may leave the schema to the search path. Searching for the name alone finds
/// all of them; two tables with one name in two schemas are then reported together, which costs a re-run and never
/// misses one.
/// <para>
/// A delete reaches further than the table it names. The database removes or clears the rows that refer to a deleted
/// row by itself (ON DELETE CASCADE, ON DELETE SET NULL), with no command of their own, so the catalog also knows,
/// from the model's foreign keys, which tables a delete from a table can change. A statement that replaces a row
/// under its key is such a delete where the database deletes the old row to make room.
/// </para>
/// </remarks>
/// <param name="maxCachedCommands">How many command texts each model remembers the tables of.</param>
internal sealed class LiveTableCatalog(int maxCachedCommands)
{
    // A context type can serve several models (one per tenant schema, for example), each with its own table names.
    private readonly ConditionalWeakTable<IModel, ModelTables> _models = [];

    /// <summary>Returns the keys of every mapped table and view <paramref name="commandText" /> names.</summary>
    public string[] Scan(DbContext context, string commandText) => _For(context).Scan(commandText);

    /// <summary>
    /// Returns the keys of every table a write with <paramref name="commandText" /> can change: the tables it
    /// names, and for a text that can delete rows, the tables whose rows the database removes or clears with them.
    /// </summary>
    public string[] ScanWrite(DbContext context, string commandText) => _For(context).ScanWrite(commandText);

    /// <summary>
    /// Returns the keys of every table <paramref name="entityType" /> is stored in: one for most types, several for
    /// a type in a table-per-type hierarchy or split across tables.
    /// </summary>
    public string[] KeysOf(DbContext context, IEntityType entityType) => _For(context).KeysOf(entityType);

    /// <summary>
    /// Returns the keys of every table whose rows the database removes or clears when a row of
    /// <paramref name="entityType" /> is deleted: the tables of the types that refer to it through a foreign key
    /// that cascades or sets null, and of the types that refer to those through a cascade, to any depth.
    /// </summary>
    public string[] DependentKeysOf(DbContext context, IEntityType entityType)
    {
        return _For(context).DependentKeysOf(entityType);
    }

    private ModelTables _For(DbContext context)
    {
        var model = context.Model;

        if (_models.TryGetValue(model, out var tables))
        {
            return tables;
        }

        var created = new ModelTables(model, context.GetService<ISqlGenerationHelper>(), maxCachedCommands);

        return _models.GetValue(model, _ => created);
    }

    private sealed class ModelTables
    {
        private readonly (string Delimited, string Key)[] _tables;
        private readonly int _maxCachedCommands;
        private readonly ConcurrentDictionary<string, CommandTables> _byCommandText = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<IEntityType, string[]> _byEntityType = new();
        private readonly ConcurrentDictionary<IEntityType, string[]> _dependentsByEntityType = new();
        private readonly Lazy<Dictionary<string, string[]>> _deletedWith;

        public ModelTables(IModel model, ISqlGenerationHelper sql, int maxCachedCommands)
        {
            _maxCachedCommands = maxCachedCommands;
            _deletedWith = new Lazy<Dictionary<string, string[]>>(() => _FindTablesDeletedWith(model));

            var relational = model.GetRelationalModel();
            var relations = relational
                .Tables.Select(table => (table.Name, table.Schema))
                .Concat(relational.Views.Select(view => (view.Name, view.Schema)))
                .Distinct();

            _tables =
            [
                .. relations.Select(relation =>
                    (sql.DelimitIdentifier(relation.Name), LiveTableKey.Create(relation.Name, relation.Schema))
                ),
            ];
        }

        public string[] Scan(string commandText) => _Tables(commandText).Named;

        public string[] ScanWrite(string commandText) => _Tables(commandText).Written;

        private CommandTables _Tables(string commandText)
        {
            if (_byCommandText.TryGetValue(commandText, out var cached))
            {
                return cached;
            }

            var keys = new List<string>();

            foreach (var (delimited, key) in _tables)
            {
                if (commandText.Contains(delimited, StringComparison.Ordinal))
                {
                    keys.Add(key);
                }
            }

            string[] named = [.. keys];
            var found = new CommandTables(named, _CanDeleteRows(commandText) ? _WithTablesDeletedWith(named) : named);

            // Raw SQL can be different on every call and EF Core writes one text per collection-parameter size, so
            // the memory is bounded; emptying it costs a few repeated searches and keeps the texts in current use.
            if (_byCommandText.Count >= _maxCachedCommands)
            {
                _byCommandText.Clear();
            }

            _byCommandText[commandText] = found;

            return found;
        }

        public string[] DependentKeysOf(IEntityType entityType)
        {
            return _dependentsByEntityType.GetOrAdd(entityType, _FindDependentKeys);
        }

        // The model's delete behavior says what EF Core does, not what the database was created to do: a schema
        // written by hand may cascade where the model only cascades tracked entities. So the client-side behaviors
        // count as well; announcing a table nothing changed costs one re-run.
        private string[] _FindDependentKeys(IEntityType principal)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<IEntityType> { principal };
            var pending = new Stack<IEntityType>([principal]);

            while (pending.TryPop(out var type))
            {
                foreach (var foreignKey in type.GetReferencingForeignKeys())
                {
                    var removesRows =
                        foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade;

                    if (
                        !removesRows
                        && foreignKey.DeleteBehavior is not (DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull)
                    )
                    {
                        continue;
                    }

                    // A row of a derived type is a row of the type the foreign key is declared on.
                    foreach (var dependent in foreignKey.DeclaringEntityType.GetDerivedTypesInclusive())
                    {
                        keys.UnionWith(KeysOf(dependent));

                        // A cleared row stays, so nothing that refers to it is touched; a removed row takes its
                        // own dependents with it.
                        if (removesRows && visited.Add(dependent))
                        {
                            pending.Push(dependent);
                        }
                    }
                }
            }

            return [.. keys];
        }

        // A command names tables, not entity types, and several types can share a table (a hierarchy in one table,
        // an owned type). A delete from a table is taken as a delete of every type stored in it, and it can also
        // remove the rows the same entities keep in their other tables.
        private Dictionary<string, string[]> _FindTablesDeletedWith(IModel model)
        {
            var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var entityType in model.GetEntityTypes())
            {
                var own = KeysOf(entityType);
                var dependents = DependentKeysOf(entityType);

                foreach (var key in own)
                {
                    if (!found.TryGetValue(key, out var keys))
                    {
                        found[key] = keys = new HashSet<string>(StringComparer.Ordinal);
                    }

                    keys.UnionWith(own);
                    keys.UnionWith(dependents);
                }
            }

            return found.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        }

        private string[] _WithTablesDeletedWith(string[] named)
        {
            var keys = new HashSet<string>(named, StringComparer.Ordinal);

            foreach (var key in named)
            {
                if (_deletedWith.Value.TryGetValue(key, out var deleted))
                {
                    keys.UnionWith(deleted);
                }
            }

            return keys.Count == named.Length ? named : [.. keys];
        }

        // The keyword anywhere in the text, not only at its start: a delete may follow a comment, a common table
        // expression, or another statement of a batch, and MERGE deletes too. REPLACE counts because SQLite
        // (INSERT OR REPLACE, REPLACE INTO) and MySQL (REPLACE INTO) resolve a key conflict by deleting the old
        // row, which runs the foreign keys' delete actions although the statement inserts. A text that only
        // mentions a word, as the name of a column or the replace() function for example, is taken for a delete,
        // which costs a re-run and misses nothing.
        private static bool _CanDeleteRows(string commandText)
        {
            return _HasWord(commandText, "DELETE")
                || _HasWord(commandText, "TRUNCATE")
                || _HasWord(commandText, "REPLACE");
        }

        private static bool _HasWord(string text, string word)
        {
            var from = 0;

            while (true)
            {
                var at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);

                if (at < 0)
                {
                    return false;
                }

                var end = at + word.Length;

                if (
                    (at == 0 || !_IsWordCharacter(text[at - 1])) && (end == text.Length || !_IsWordCharacter(text[end]))
                )
                {
                    return true;
                }

                from = end;
            }
        }

        private static bool _IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

        /// <summary>The tables of one command text.</summary>
        /// <param name="Named">The tables the text names.</param>
        /// <param name="Written">The tables a write with this text can change.</param>
        private sealed record CommandTables(string[] Named, string[] Written);

        public string[] KeysOf(IEntityType entityType)
        {
            return _byEntityType.GetOrAdd(
                entityType,
                static type =>
                    [
                        .. type.GetTableMappings()
                            .Select(mapping => LiveTableKey.Create(mapping.Table.Name, mapping.Table.Schema))
                            .Distinct(StringComparer.Ordinal),
                    ]
            );
        }
    }
}
