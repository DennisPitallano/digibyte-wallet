namespace DigiByte.Web.Services;

/// <summary>How much of Baku the user wants to see. Off hides Info/Tip and all mascot imagery, but
/// Warning and Critical messages always show as text.</summary>
public enum BakuMode { Full, GuardianOnly, Off }

/// <summary>Ordered by priority: a higher value wins when several messages compete for one screen.</summary>
public enum BakuSeverity { Info = 0, Tip = 1, Warning = 2, Critical = 3 }

/// <summary>A predefined English message tied to a screen or situation (specs/002-baku-mascot-guide/data-model.md).</summary>
public sealed record BakuMessage(
    string Id,
    BakuSeverity Severity,
    string PoseId,
    string Title,
    string Body,
    bool RequiresConfirmation = false)
{
    /// <summary>Only Info and Tip can be dismissed; warnings are never permanently dismissible.</summary>
    public bool Dismissible => Severity is BakuSeverity.Info or BakuSeverity.Tip;
}

/// <summary>Baku's poses and message catalog. English only in this release.</summary>
public static class BakuCatalog
{
    public const string PoseFolder = "images/mascot/poses/";

    public static string PosePath(string poseId) => $"{PoseFolder}{poseId}.webp";

    public static IReadOnlyList<BakuMessage> All { get; } =
    [
        new("welcome.greeting", BakuSeverity.Info, "open-arms", "Hi, I am Baku!", "I will help you set up your wallet and keep it safe."),
        new("create.intro", BakuSeverity.Info, "welcome", "Let us create your wallet", "It takes a minute. You will get a recovery phrase that only you should ever see."),
        new("recover.intro", BakuSeverity.Info, "thinking", "Restoring a wallet", "Enter your recovery phrase carefully, word by word, in the right order."),
        new("backup.why", BakuSeverity.Info, "trustworthy", "Write it down", "These words are the only way to restore your wallet. Store them offline and never share them."),
        new("backup.done", BakuSeverity.Info, "celebrating", "Backup confirmed!", "Great job. Your wallet is ready."),
        new("wallet.created", BakuSeverity.Info, "celebrating", "Wallet created!", "You are all set to receive your first DGB."),
        new("unlock.greeting", BakuSeverity.Info, "greeting", "Good to see you", "Enter your PIN to unlock your wallet."),
        new("seed.reveal", BakuSeverity.Critical, "locked", "Anyone can see your screen", "Nobody legitimate will ever ask for your recovery phrase. Make sure no one is watching.", RequiresConfirmation: true),
        new("send.new-address", BakuSeverity.Warning, "guard", "New address", "You have not sent to this address before. Double-check every character with the recipient.", RequiresConfirmation: true),
        new("send.large-amount", BakuSeverity.Warning, "protective", "Large send", "This is half or more of your balance. Make sure the amount is right.", RequiresConfirmation: true),
        new("send.new-and-large", BakuSeverity.Warning, "protective", "Check this send", "New address, and half or more of your balance. Confirm both before continuing.", RequiresConfirmation: true),
        new("send.done", BakuSeverity.Info, "approve", "Sent!", "Your transaction is on its way."),
        new("send.error", BakuSeverity.Info, "error", "That did not work", "Nothing was sent. Check the details and try again."),
        new("p2p.verify", BakuSeverity.Warning, "guard", "Trade carefully", "Verify who you trade with. Never share your recovery phrase or pay outside the trade flow."),
        new("home.empty", BakuSeverity.Tip, "no-funds", "No transactions yet", "Use Receive to get your first DGB."),
        new("home.tip", BakuSeverity.Tip, "idea", "Tip: back up first", "Keep your recovery phrase safe before adding funds."),
        new("receive.tip", BakuSeverity.Tip, "idea", "Share your address", "Each address can be reused, but a fresh one improves privacy."),
        new("dd.tip", BakuSeverity.Tip, "idea", "About DigiDollar", "DigiDollar is a stable-value asset on DigiByte. Send and receive it like DGB."),
        new("help.intro", BakuSeverity.Info, "support", "How can I help?", "Search or pick a topic below. I will guide you through it."),
        new("help.no-results", BakuSeverity.Info, "thinking", "Nothing found", "Try different words, or browse the topics below."),
        new("digiid.intro", BakuSeverity.Info, "idea", "Log in without a password", "Digi-ID proves it is you with a signature. No funds move and your main keys stay private."),
        new("digiid.new-site", BakuSeverity.Warning, "guard", "First time with this site", "You have not signed in here before. Only approve if you started this login yourself and the website name looks right.", RequiresConfirmation: true),
        new("digiid.insecure", BakuSeverity.Warning, "protective", "This site is not secure", "It uses plain HTTP, so the login could be intercepted. Approve only if you fully trust the site.", RequiresConfirmation: true),
        new("settings.tip", BakuSeverity.Tip, "idea", "Make it yours", "You can change how much you see of me in Baku settings."),
    ];

    private static readonly Dictionary<string, BakuMessage> ById = All.ToDictionary(m => m.Id);

    public static BakuMessage? Find(string id) => ById.GetValueOrDefault(id);
}
