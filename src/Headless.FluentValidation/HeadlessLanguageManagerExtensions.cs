// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace FluentValidation.Resources;

/// <summary>Headless translations for FluentValidation's built-in error messages.</summary>
[PublicAPI]
public static class HeadlessLanguageManagerExtensions
{
    private const string _EgyptianArabic = "ar-EG";

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

    /// <summary>
    /// Adds Egyptian Arabic (<c>ar-EG</c>) wording for every FluentValidation built-in message, so a request whose UI
    /// culture is <c>ar-EG</c> reads Egyptian Arabic while a plain <c>ar</c> request keeps the library's formal Arabic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The language manager tries the specific culture before its parent, so these entries win over the formal
    /// Arabic FluentValidation ships under <c>ar</c>, and any other <c>ar-*</c> culture still falls back to it.
    /// </para>
    /// <para>
    /// The messages leave out <c>{PropertyName}</c>: it resolves to the property's English display name, which reads
    /// badly inside an Arabic sentence, and clients show a field error next to the field it belongs to.
    /// </para>
    /// <para>
    /// A translation added for the same culture and key afterwards replaces the one added here.
    /// </para>
    /// </remarks>
    /// <param name="languageManager">The language manager to add the translations to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="languageManager"/> is <see langword="null"/>.</exception>
    public static void AddHeadlessEgyptianArabicTranslations(this LanguageManager languageManager)
    {
        Argument.IsNotNull(languageManager);

        foreach (var (key, message) in _EgyptianArabicMessages)
        {
            languageManager.AddTranslation(_EgyptianArabic, key, message);
        }
    }
}
