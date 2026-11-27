namespace DigiByte.Pay.Web.Components.Pages.Merchant;

/// <summary>
/// The words around the store's DigiDollar setting (specs/033 User Story 5, specs/035 FR-019). They list only the places
/// that really offer DD (FR-017), say DD goes straight to the merchant's own wallet, like DGB, and the on/off messages
/// name the places (FR-018). <c>ddSurfacesEnabled</c> is the server switch from <c>/v1/pay/me</c>: off, only the API
/// offers DD; on, every surface of spec 033 does.
/// </summary>
public static class DigiDollarSettingText
{
    /// <summary>Where DD is offered, as a phrase that fits after "through".</summary>
    public static string Places(bool ddSurfacesEnabled) => ddSurfacesEnabled
        ? "the POS till, payment links and one-off payments, the buyer's DGB or DD choice at checkout, the donation page and the API"
        : "the DigiPay API (payments your own code creates with currency DD)";

    public static string Description(bool ddSurfacesEnabled) => ddSurfacesEnabled
        ? $"Accept USD-pegged DigiDollar alongside DGB. 1 DD = $1. Buyers can pay in DD through {Places(true)}. "
          + NoCustody
        : $"Accept USD-pegged DigiDollar alongside DGB. 1 DD = $1. For now, DD is offered only through {Places(false)}. "
          + NoCustody;

    /// <summary>specs/035: DD is paid to addresses from the merchant's own key, so DigiPay never holds it.</summary>
    public const string NoCustody =
        "DigiDollar goes straight to your wallet, like DGB: each payment gets a new address from your DigiDollar key, "
        + "and DigiPay never holds it. Add the key from your DigiByte Wallet below.";

    public static string EnabledMessage(bool ddSurfacesEnabled) =>
        $"DigiDollar is on. Buyers can pay in DD through {Places(ddSurfacesEnabled)}.";

    public static string DisabledMessage(bool ddSurfacesEnabled) =>
        $"DigiDollar is off. New payments through {Places(ddSurfacesEnabled)} are DGB only. Payments already created in DD will still complete.";
}
