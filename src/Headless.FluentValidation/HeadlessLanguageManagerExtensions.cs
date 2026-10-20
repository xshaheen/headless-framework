// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace FluentValidation.Resources;

/// <summary>Headless translations for FluentValidation's built-in error messages.</summary>
[PublicAPI]
public static class HeadlessLanguageManagerExtensions
{
    private const string _EgyptianArabic = "ar-EG";
    private const string _Arabic = "ar";

    // Keys are FluentValidation's built-in message keys. IsEnumName has no key of its own: it reads EnumValidator.
    private static readonly (string Key, string Message)[] _EgyptianArabicMessages =
    [
        ("NotNullValidator", "الحقل ده مطلوب."),
        ("NotEmptyValidator", "الحقل ده مطلوب."),
        ("EmptyValidator", "الحقل ده لازم يفضل فاضي."),
        ("NullValidator", "الحقل ده لازم يفضل فاضي."),
        ("EmailValidator", "الإيميل ده مش صحيح."),
        ("RegularExpressionValidator", "الصيغة مش صحيحة."),
        ("CreditCardValidator", "رقم الكارت ده مش صحيح."),
        ("EnumValidator", "\"{PropertyValue}\" مش من الاختيارات المتاحة."),
        ("PredicateValidator", "القيمة دي مش مطابقة للشروط المطلوبة."),
        ("AsyncPredicateValidator", "القيمة دي مش مطابقة للشروط المطلوبة."),
        ("EqualValidator", "القيمة لازم تساوي {ComparisonValue}."),
        ("NotEqualValidator", "القيمة لازم متساويش {ComparisonValue}."),
        ("GreaterThanValidator", "القيمة لازم تكون أكبر من {ComparisonValue}."),
        ("GreaterThanOrEqualValidator", "القيمة لازم تكون {ComparisonValue} أو أكتر."),
        ("LessThanValidator", "القيمة لازم تكون أقل من {ComparisonValue}."),
        ("LessThanOrEqualValidator", "القيمة لازم تكون {ComparisonValue} أو أقل."),
        ("InclusiveBetweenValidator", "القيمة لازم تكون من {From} لـ {To} (القيمة الحالية {PropertyValue})."),
        ("InclusiveBetween_Simple", "القيمة لازم تكون من {From} لـ {To}."),
        ("ExclusiveBetweenValidator", "القيمة لازم تكون أكبر من {From} وأقل من {To} (القيمة الحالية {PropertyValue})."),
        ("LengthValidator", "لازم يكون من {MinLength} لـ {MaxLength} حرف (عدد الحروف الحالي {TotalLength})."),
        ("Length_Simple", "لازم يكون من {MinLength} لـ {MaxLength} حرف."),
        ("MinimumLengthValidator", "لازم يكون {MinLength} حرف على الأقل (عدد الحروف الحالي {TotalLength})."),
        ("MinimumLength_Simple", "لازم يكون {MinLength} حرف على الأقل."),
        ("MaximumLengthValidator", "لازم ميزيدش عن {MaxLength} حرف (عدد الحروف الحالي {TotalLength})."),
        ("MaximumLength_Simple", "لازم ميزيدش عن {MaxLength} حرف."),
        ("ExactLengthValidator", "لازم يكون {MaxLength} حرف بالظبط (عدد الحروف الحالي {TotalLength})."),
        ("ExactLength_Simple", "لازم يكون {MaxLength} حرف بالظبط."),
        (
            "ScalePrecisionValidator",
            "الرقم لازم ميزيدش عن {ExpectedPrecision} خانة، منهم {ExpectedScale} بعد العلامة العشرية. "
                + "الرقم الحالي فيه {Digits} خانة و{ActualScale} بعد العلامة العشرية."
        ),
    ];

    // The same keys as the Egyptian set. Counts read "عدد الأحرف" rather than "{n} حرف" because Arabic number agreement
    // changes the noun's form with the value, which a fixed template cannot follow.
    private static readonly (string Key, string Message)[] _ArabicMessages =
    [
        ("NotNullValidator", "هذا الحقل مطلوب."),
        ("NotEmptyValidator", "هذا الحقل مطلوب."),
        ("EmptyValidator", "يجب أن يكون هذا الحقل فارغاً."),
        ("NullValidator", "يجب أن يكون هذا الحقل فارغاً."),
        ("EmailValidator", "البريد الإلكتروني غير صحيح."),
        ("RegularExpressionValidator", "الصيغة غير صحيحة."),
        ("CreditCardValidator", "رقم البطاقة غير صحيح."),
        ("EnumValidator", "القيمة \"{PropertyValue}\" ليست من الخيارات المتاحة."),
        ("PredicateValidator", "القيمة لا تطابق الشروط المطلوبة."),
        ("AsyncPredicateValidator", "القيمة لا تطابق الشروط المطلوبة."),
        ("EqualValidator", "يجب أن تساوي القيمة {ComparisonValue}."),
        ("NotEqualValidator", "يجب ألا تساوي القيمة {ComparisonValue}."),
        ("GreaterThanValidator", "يجب أن تكون القيمة أكبر من {ComparisonValue}."),
        ("GreaterThanOrEqualValidator", "يجب أن تكون القيمة {ComparisonValue} أو أكثر."),
        ("LessThanValidator", "يجب أن تكون القيمة أقل من {ComparisonValue}."),
        ("LessThanOrEqualValidator", "يجب أن تكون القيمة {ComparisonValue} أو أقل."),
        ("InclusiveBetweenValidator", "يجب أن تكون القيمة بين {From} و{To} (القيمة الحالية {PropertyValue})."),
        ("InclusiveBetween_Simple", "يجب أن تكون القيمة بين {From} و{To}."),
        (
            "ExclusiveBetweenValidator",
            "يجب أن تكون القيمة أكبر من {From} وأقل من {To} (القيمة الحالية {PropertyValue})."
        ),
        ("LengthValidator", "يجب أن يكون عدد الأحرف بين {MinLength} و{MaxLength} (عدد الأحرف الحالي {TotalLength})."),
        ("Length_Simple", "يجب أن يكون عدد الأحرف بين {MinLength} و{MaxLength}."),
        ("MinimumLengthValidator", "يجب ألا يقل عدد الأحرف عن {MinLength} (عدد الأحرف الحالي {TotalLength})."),
        ("MinimumLength_Simple", "يجب ألا يقل عدد الأحرف عن {MinLength}."),
        ("MaximumLengthValidator", "يجب ألا يزيد عدد الأحرف عن {MaxLength} (عدد الأحرف الحالي {TotalLength})."),
        ("MaximumLength_Simple", "يجب ألا يزيد عدد الأحرف عن {MaxLength}."),
        ("ExactLengthValidator", "يجب أن يكون عدد الأحرف {MaxLength} بالضبط (عدد الأحرف الحالي {TotalLength})."),
        ("ExactLength_Simple", "يجب أن يكون عدد الأحرف {MaxLength} بالضبط."),
        (
            "ScalePrecisionValidator",
            "يجب ألا يزيد عدد خانات الرقم عن {ExpectedPrecision}، منها {ExpectedScale} بعد الفاصلة العشرية. "
                + "عدد خانات الرقم الحالي {Digits}، منها {ActualScale} بعد الفاصلة العشرية."
        ),
    ];

    /// <summary>
    /// Adds Egyptian Arabic (<c>ar-EG</c>) wording for every FluentValidation built-in message, so a request whose UI
    /// culture is <c>ar-EG</c> reads Egyptian Arabic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The language manager tries the specific culture before its parent, so these entries win over the Arabic under
    /// <c>ar</c>, and any other <c>ar-*</c> culture still falls back to <c>ar</c>.
    /// </para>
    /// <para>
    /// The messages leave out <c>{PropertyName}</c>: it resolves to the property's English display name, which reads
    /// badly inside an Arabic sentence, and clients show a field error next to the field it belongs to.
    /// </para>
    /// <para>
    /// Each entry replaces whatever text the manager holds for <c>ar-EG</c> and that key. Add your own <c>ar-EG</c>
    /// translations after this call: one added before it is overwritten.
    /// </para>
    /// </remarks>
    /// <param name="languageManager">The language manager to add the translations to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="languageManager"/> is <see langword="null"/>.</exception>
    public static void AddHeadlessEgyptianArabicTranslations(this LanguageManager languageManager)
    {
        Argument.IsNotNull(languageManager);
        _AddTranslations(languageManager, _EgyptianArabic, _EgyptianArabicMessages);
    }

    /// <summary>
    /// Replaces the formal Arabic FluentValidation ships under <c>ar</c> with the Headless wording for every built-in
    /// message, so <c>ar</c> and every Arabic region without its own entries read the same text as the Headless
    /// resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The messages leave out <c>{PropertyName}</c>: it resolves to the property's English display name, which reads
    /// badly inside an Arabic sentence, and clients show a field error next to the field it belongs to.
    /// </para>
    /// <para>
    /// Each entry replaces whatever text the manager holds for <c>ar</c> and that key. Add your own <c>ar</c>
    /// translations after this call: one added before it is overwritten.
    /// </para>
    /// </remarks>
    /// <param name="languageManager">The language manager to add the translations to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="languageManager"/> is <see langword="null"/>.</exception>
    public static void AddHeadlessArabicTranslations(this LanguageManager languageManager)
    {
        Argument.IsNotNull(languageManager);
        _AddTranslations(languageManager, _Arabic, _ArabicMessages);
    }

    private static void _AddTranslations(
        LanguageManager languageManager,
        string culture,
        (string Key, string Message)[] messages
    )
    {
        foreach (var (key, message) in messages)
        {
            languageManager.AddTranslation(culture, key, message);
        }
    }
}
