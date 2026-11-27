namespace DigiByte.Pay.Web.Components.Pages.Merchant;

/// <summary>
/// The words around the store's DigiDollar setting (specs/033 User Story 5). They list only the places that really
/// offer DD (FR-017), always say that DigiPay holds the DD buyers pay (FR-031), and the on/off messages name the
/// places (FR-018). <c>ddSurfacesEnabled</c> is the server switch from <c>/v1/pay/me</c>: off, only the API offers DD;
/// on, every surface of spec 033 does. Payouts exist either way (spec 034 FR-040), so neither says DD can't be withdrawn.
/// </summary>
public static class DigiDollarSettingText
{
    /// <summary>Where DD is offered, as a phrase that fits after "through".</summary>
    public static string Places(bool ddSurfacesEnabled) => ddSurfacesEnabled
        ? "the POS till, payment links and one-off payments, the buyer's DGB or DD choice at checkout, the donation page and the API"
        : "the DigiPay API (payments your own code creates with currency DD)";

    public static string Description(bool ddSurfacesEnabled) => ddSurfacesEnabled
        ? $"Accept USD-pegged DigiDollar alongside DGB. 1 DD = $1. Buyers can pay in DD through {Places(true)}. "
          + "DigiPay holds the DD buyers pay until it's paid out to your payout address. DigiPay pays the network fees in DGB."
        : $"Accept USD-pegged DigiDollar alongside DGB. 1 DD = $1. For now, DD is offered only through {Places(false)}. "
          + "DigiPay holds the DD buyers pay until it's paid out to your payout address. "
          + "DigiPay pays the network fees in DGB.";

    public static string EnabledMessage(bool ddSurfacesEnabled) =>
        $"DigiDollar is on. Buyers can pay in DD through {Places(ddSurfacesEnabled)}.";

    public static string DisabledMessage(bool ddSurfacesEnabled) =>
        $"DigiDollar is off. New payments through {Places(ddSurfacesEnabled)} are DGB only. Payments already created in DD will still complete.";
}
