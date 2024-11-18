// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Payments.Paymob.Services.Resources;

public static class PaymobMessageDescriptor
{
    public static class General
    {
        public static ErrorDescriptor NoVodafoneCash()
        {
            return new("cash:no_vodafone_cash", CashInMessages.CashNoVodafoneCash);
        }

        public static ErrorDescriptor NoEtisalatCash()
        {
            return new("cash:no_etisalat_cash", CashInMessages.CashNoEtisalatCash);
        }

        public static ErrorDescriptor InvalidVodafoneNumber()
        {
            return new("cash:invalid_vodafone_number", CashInMessages.CashInvalidVodafoneNumber);
        }

        public static ErrorDescriptor InvalidEtisalatNumber()
        {
            return new("cash:invalid_etisalat_number", CashInMessages.CashInvalidEtisalatNumber);
        }

        public static ErrorDescriptor InvalidOrangeNumber()
        {
            return new("cash:invalid_orange_number", CashInMessages.CashInvalidOrangeNumber);
        }

        public static ErrorDescriptor NotValidWalletPhoneNumber()
        {
            return new("cash:invalid_wallet_phone_number", CashInMessages.CashNotValidWalletPhoneNumber);
        }
    }

    public static class CashIn
    {
        public static ErrorDescriptor ProviderConnectionFailed()
        {
            return new("cashIn:provider_connection_failed", CashInMessages.CashInProviderConnectionFailed);
        }
    }

    public static class CashOut
    {
        public static ErrorDescriptor ProviderConnectionFailed()
        {
            return new("cash_out:provider_connection_failed", CashInMessages.CashProviderConnectionFailed);
        }

        public static ErrorDescriptor ProviderIsDown()
        {
            return new("cash_out:provider_down", CashInMessages.CashOutProviderIsDown);
        }

        public static ErrorDescriptor InsufficientFunds()
        {
            return new("cash_out:insufficient_funds", CashInMessages.CashOutInsufficientFunds);
        }

        public static ErrorDescriptor InvalidRequest()
        {
            return new("cash_out:invalid_request", CashInMessages.CashOutInvalidRequest);
        }
    }
}
