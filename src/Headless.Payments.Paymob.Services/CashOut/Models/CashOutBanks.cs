// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Models;

public sealed record CashOutBanks(string Name, string Code)
{
    public static IReadOnlyList<CashOutBanks> All { get; } =
    [
        new("Ahli United Bank", "AUB"),
        new("Citi Bank N.A. Egypt", "CITI"),
        new("MIDBANK", "MIDB"),
        new("Banque Du Caire", "BDC"),
        new("HSBC Bank Egypt S.A.E", "HSBC"),
        new("Credit Agricole Egypt S.A.E", "CAE"),
        new("Egyptian Gulf Bank", "EGB"),
        new("The United Bank", "UB"),
        new("Qatar National Bank Alahli", "QNB"),
        new("Central Bank Of Egypt", "BBE"),
        new("Arab Bank PLC", "ARAB"),
        new("Emirates National Bank of Dubai", "ENBD"),
        new("Al Ahli Bank of Kuwait – Egypt", "ABK"),
        new("National Bank of Kuwait – Egypt", "NBK"),
        new("Arab Banking Corporation - Egypt S.A.E", "ABC"),
        new("First Abu Dhabi Bank", "FAB"),
        new("Abu Dhabi Islamic Bank – Egypt", "ADIB"),
        new("Commercial International Bank - Egypt S.A.E", "CIB"),
        new("Housing And Development Bank", "HDB"),
        new("Banque Misr", "MISR"),
        new("Arab African International Bank", "AAIB"),
        new("Egyptian Arab Land Bank", "EALB"),
        new("Export Development Bank of Egypt", "EDBE"),
        new("Faisal Islamic Bank of Egypt", "FAIB"),
        new("Blom Bank", "BLOM"),
        new("Abu Dhabi Commercial Bank – Egypt", "ADCB"),
        new("Alex Bank Egypt", "BOA"),
        new("Societe Arabe Internationale De Banque", "SAIB"),
        new("National Bank of Egypt", "NBE"),
        new("Al Baraka Bank Egypt B.S.C.", "ABRK"),
        new("Egypt Post", "POST"),
        new("Nasser Social Bank", "NSB"),
        new("Industrial Development Bank", "IDB"),
        new("Suez Canal Bank", "SCB"),
        new("Mashreq Bank", "MASH"),
        new("Arab Investment Bank", "AIB"),
        new("Audi Bank", "AUDI"),
        new("General Authority For Supply Commodities", "GASC"),
        new("National Bank of Egypt - EGPA", "EGPA"),
        new("Arab International Bank", "ARIB"),
        new("Agricultural Bank of Egypt", "PDAC"),
        new("National Bank of Greece", "NBG"),
        new("Central Bank Of Egypt", "CBE"),
    ];
}
