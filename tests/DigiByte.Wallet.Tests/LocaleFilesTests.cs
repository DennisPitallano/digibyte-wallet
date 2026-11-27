using System.Text.Json;
using System.Text.RegularExpressions;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Guards on the translation files (spec 028): nothing empty, placeholders that match between languages,
/// every key the screens ask for exists in English, and only languages with a file are offered.
/// </summary>
public partial class LocaleFilesTests
{
    private static readonly string Root = FindRepoRoot();
    private static readonly string LocalesDir = Path.Combine(Root, "src", "DigiByte.Web", "wwwroot", "locales");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "src.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root (src/src.sln).");
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LocalesDir, code + ".json")))!;

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static SortedSet<string> Placeholders(string text) =>
        new(Placeholder().Matches(text).Select(m => m.Groups[1].Value));

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void NoTranslationIsEmpty(string code)
    {
        var empty = Load(code).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(empty.Count == 0, $"{code}.json has empty values: {string.Join(", ", empty)}");
    }

    [Fact]
    public void PlaceholdersMatchBetweenEnglishAndSpanish()
    {
        var en = Load("en");
        var es = Load("es");
        var mismatched = en.Keys.Intersect(es.Keys)
            .Where(k => !Placeholders(en[k]).SetEquals(Placeholders(es[k])))
            .ToList();
        Assert.True(mismatched.Count == 0, $"Placeholders differ for: {string.Join(", ", mismatched)}");
    }

    [GeneratedRegex(@"\b(?:L|l|_loc)\.T\(\s*""([^""]+)""")]
    private static partial Regex KeyUse();

    [Fact]
    public void EveryKeyTheScreensUseExistsInEnglish()
    {
        var en = Load("en");
        var webDir = Path.Combine(Root, "src", "DigiByte.Web");
        var missing = Directory.EnumerateFiles(webDir, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => KeyUse().Matches(File.ReadAllText(f))
                .Select(m => (Key: m.Groups[1].Value, File: Path.GetRelativePath(webDir, f))))
            .Where(u => !en.ContainsKey(u.Key))
            .Select(u => $"{u.Key} ({u.File})")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, $"Keys missing from en.json: {string.Join(", ", missing)}");
    }

    [Fact]
    public void OnlyEnglishAndSpanishAreOffered()
    {
        Assert.Equal(["en", "es"], LocalizationService.SupportedLocales.Keys);
        Assert.Equal("English", LocalizationService.SupportedLocales["en"]);
        Assert.Equal("Español", LocalizationService.SupportedLocales["es"]);
    }

    [Fact]
    public void EveryOfferedLanguageHasAFile_AndEveryFileIsOffered()
    {
        var files = Directory.EnumerateFiles(LocalesDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).Order().ToList();
        var offered = LocalizationService.SupportedLocales.Keys.Order().ToList();
        Assert.Equal(offered, files);
    }

    // ---------- Translations rendered as raw HTML ----------

    /// <summary>
    /// Keys a screen renders as raw HTML (MarkupString), to keep a word bold inside a sentence. Their text is not encoded,
    /// so a translator who drops a tag or adds a stray "&lt;" or "&amp;" breaks the screen. Any other key shows its text as is.
    /// </summary>
    private static readonly string[] RawHtmlKeys =
    [
        "send.batch_info.intro", "send.fee_info.intro", "send.fee_info.footer",
        "settings.install.ios.step1", "settings.install.ios.step2", "settings.install.ios.step3",
        "settings.install.menu.step2", "settings.multisig.hint",
    ];

    private static readonly string[] AllowedTags = ["<strong>", "</strong>", "<b>", "</b>"];

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")] private static partial Regex Tag();

    [Fact]
    public void RawHtmlTranslations_HaveTheSameTagsInBothLanguages_AndNothingElseThatIsHtml()
    {
        var en = Load("en");
        var es = Load("es");
        var problems = new List<string>();
        foreach (var key in RawHtmlKeys)
        {
            foreach (var (lang, text) in new[] { ("en", en[key]), ("es", es[key]) })
            {
                var tags = Tag().Matches(text).Select(m => m.Value).ToList();
                if (tags.Any(t => !AllowedTags.Contains(t))) problems.Add($"{lang} {key}: tag other than <strong>/<b>");
                var rest = Tag().Replace(text, "");
                if (rest.Contains('<') || rest.Contains('>') || rest.Contains('&')) problems.Add($"{lang} {key}: a bare <, > or &");
            }
            var enTags = Tag().Matches(en[key]).Select(m => m.Value);
            var esTags = Tag().Matches(es[key]).Select(m => m.Value);
            if (!enTags.SequenceEqual(esTags)) problems.Add($"{key}: tags differ between en and es");
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void OnlyRawHtmlKeysContainTags()
    {
        // A tag in any other key would show on screen as literal "<strong>" text.
        var offenders = new[] { ("en", Load("en")), ("es", Load("es")) }
            .SelectMany(l => l.Item2.Where(kv => !RawHtmlKeys.Contains(kv.Key) && Tag().IsMatch(kv.Value)).Select(kv => $"{l.Item1} {kv.Key}"))
            .ToList();
        Assert.True(offenders.Count == 0, $"Tags outside the raw-HTML keys: {string.Join(", ", offenders)}");
    }

    // ---------- Date patterns kept in the translation files ----------

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("es", "es")]
    public void DatePatterns_FormatARealDate(string lang, string culture)
    {
        var date = new DateTime(2026, 11, 25, 14, 5, 0);
        var info = System.Globalization.CultureInfo.GetCultureInfo(culture);
        var problems = new List<string>();
        foreach (var (key, pattern) in Load(lang).Where(kv => kv.Key.StartsWith("history.format.")))
        {
            string text;
            try { text = date.ToString(pattern, info); }
            catch (FormatException) { problems.Add($"{key}: not a valid date pattern"); continue; }
            // A pattern must produce the date: the day of the month always appears, the year when the pattern asks for it.
            if (!text.Contains("25")) problems.Add($"{key}: '{text}' has no day");
            if (pattern.Contains("yyyy") && !text.Contains("2026")) problems.Add($"{key}: '{text}' has no year");
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    // ---------- SC-003 / FR-008: Spanish has every English entry ----------

    [Fact]
    public void SpanishHasExactlyTheEnglishKeys()
    {
        var en = Load("en").Keys.ToHashSet();
        var es = Load("es").Keys.ToHashSet();
        var missing = en.Except(es).Order().ToList();
        var extra = es.Except(en).Order().ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"Missing in es.json: {string.Join(", ", missing)}. Only in es.json: {string.Join(", ", extra)}");
    }

    // ---------- SC-002 / FR-009: no fixed English left on the screens that must be in Spanish ----------

    /// <summary>The screens (and the parts they show) that FR-009 requires in Spanish.</summary>
    public static readonly TheoryData<string> InScopeFiles =
    [
        "Pages/Settings.razor", "Components/Wallet/WalletProtectionCard.razor", "Components/BiometricNotice.razor",
        "Components/Shared/LanguagePickerSheet.razor",
        "Pages/Welcome.razor", "Pages/CreateWallet.razor", "Pages/BackupSeed.razor", "Pages/RecoverWallet.razor",
        "Pages/Unlock.razor", "Components/Shared/UnlockWalletSheet.razor", "Components/BiometricEnrollPrompt.razor",
        "Pages/Home.razor", "Components/Wallet/WalletInfoRow.razor", "Components/Shared/WalletPickerSheet.razor",
        "Components/Wallet/ReceiveEmptyState.razor", "Components/Wallet/SearchFurtherOffer.razor",
        "Pages/Receive.razor", "Pages/Send.razor", "Components/Shared/AmountAmbiguityChoice.razor",
        "Components/Shared/SendUnavailableNotice.razor", "Components/Shared/QrScannerModal.razor",
        "Components/Shared/SlideToConfirm.razor", "Components/Shared/TxSuccessOverlay.razor",
        "Pages/Transactions.razor", "Components/Shared/TransactionRow.razor",
        "Components/Shared/TransactionDetailModal.razor", "Components/Shared/ConfirmationBadge.razor",
        "Layout/NavMenu.razor", "Components/Shared/PageHeader.razor", "Components/Shared/OfflineBanner.razor",
        "Components/Shared/NodeDegradedBanner.razor", "Components/Shared/ToastContainer.razor", "Components/Shared/CopyValueButton.razor",
        "Components/Shared/GettingStarted.razor", "App.razor",
        "Pages/Contacts.razor", "Pages/WalletManagement.razor", "Pages/Recovery.razor", "Pages/Payments.razor",
        "Pages/Help.razor", "Layout/MainLayout.razor", "Pages/Identity.razor",
        "Components/Shared/DigiIdAddressWarning.razor", "Components/Shared/DigiIdLoginDetailModal.razor",
        "Components/Shared/DigiIdRecoverFunds.razor", "Pages/DigiDollar.razor", "Pages/DigiDollarReceive.razor",
        "Pages/DigiDollarSend.razor", "Pages/ExportDigiDollarKey.razor", "Components/DigiDollar/DdIcon.razor",
        "Components/DigiDollar/DigiDollarConfirmDialog.razor", "Components/DigiDollar/DigiDollarHealthBanner.razor",
        "Components/DigiDollar/DigiDollarHomeCard.razor", "Components/DigiDollar/DigiDollarPriceLine.razor",
        "Components/DigiDollar/DigiDollarRouteGuard.razor", "Components/DigiDollar/DigiDollarSendForm.razor",
        "Components/DigiDollar/DigiDollarUnavailableLine.razor", "Components/DigiDollar/OracleStatusPanel.razor",
        "Pages/MultisigActivity.razor", "Pages/MultisigCreate.razor", "Pages/MultisigDetail.razor",
        "Pages/MultisigGuide.razor", "Pages/MultisigImport.razor", "Pages/MultisigPending.razor",
        "Pages/MultisigPendingDetail.razor", "Pages/MultisigRoomPage.razor", "Pages/MultisigSend.razor",
        "Pages/MultisigSigningRoom.razor", "Pages/MultisigWallets.razor", "Components/Multisig/ActivityItem.razor",
        "Components/Multisig/ActivityList.razor", "Components/Multisig/AddProposalSheet.razor",
        "Components/Multisig/AttentionBadge.razor", "Components/Multisig/BackupCard.razor",
        "Components/Multisig/CoSignerRow.razor", "Components/Multisig/CollapsePanel.razor",
        "Components/Multisig/ConnectionBanner.razor", "Components/Multisig/EmptyState.razor",
        "Components/Multisig/KeyView.razor", "Components/Multisig/MultisigAccessNotice.razor",
        "Components/Multisig/MultisigBalanceCard.razor", "Components/Multisig/MultisigWalletCard.razor",
        "Components/Multisig/NeedsYouCount.razor", "Components/Multisig/PaymentSummary.razor",
        "Components/Multisig/PendingCard.razor", "Components/Multisig/QrImage.razor",
        "Components/Multisig/ReceiveSheet.razor", "Components/Multisig/RecipientInput.razor",
        "Components/Multisig/RenameDialog.razor", "Components/Multisig/RoomBar.razor",
        "Components/Multisig/RoomCodeInput.razor", "Components/Multisig/RoomHeadlineCard.razor",
        "Components/Multisig/RoomInvite.razor", "Components/Multisig/RoomMenu.razor",
        "Components/Multisig/RoomStageTracker.razor", "Components/Multisig/SharedWalletContextLine.razor",
        "Components/Multisig/SharedWalletSwitcher.razor", "Components/Multisig/SignerCard.razor",
        "Components/Multisig/SignerList.razor", "Components/Multisig/SignerProgress.razor",
        "Components/Multisig/StatusBadge.razor", "Components/Multisig/StepIndicator.razor",
        "Components/Multisig/StickyAction.razor", "Components/Multisig/UxIcon.razor",
    ];

    /// <summary>Words that are the same in every language (product and unit names, codes).</summary>
    private static readonly HashSet<string> AllowedWords = new(StringComparer.Ordinal)
    {
        "DigiByte", "DGB", "DigiDollar", "DD", "Digi", "ID", "BIP21", "PSBT", "QR", "PIN", "sat", "vB", "xpub", "USD",
        "SegWit", "Taproot", "Legacy", "mainnet", "testnet", "Baku", "Byte", "WIF", "dgb", // "Byte": the brand styled as Digi<span>Byte</span>; "dgb1…": an address prefix
    };

    [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)] private static partial Regex RazorComment();
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)] private static partial Regex HtmlComment();
    [GeneratedRegex(@"<svg\b.*?</svg>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex Svg();
    [GeneratedRegex(@"<(?:style|script)\b.*?</(?:style|script)>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex StyleOrScript();
    [GeneratedRegex(@"(?:placeholder|title|aria-label|alt)=""([^""]*)""")] private static partial Regex TextAttribute();
    [GeneratedRegex(@">([^<>]+)<")] private static partial Regex TextNode();
    [GeneratedRegex(@"[A-Za-z][A-Za-z'’]+")] private static partial Regex Word();

    [GeneratedRegex(@"^\s*@(?:inject|using|page|implements|inherits|attribute|layout|namespace|typeparam|rendermode|preservewhitespace)\b.*$", RegexOptions.Multiline)]
    private static partial Regex Directive();

    /// <summary>Index just past the brace that closes the one at <paramref name="open"/>.</summary>
    private static int PastClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var j = open; j < text.Length; j++)
        {
            if (text[j] == '{') depth++;
            else if (text[j] == '}' && --depth == 0) return j + 1;
        }
        return text.Length;
    }

    /// <summary>Removes Razor/C# from markup: @{ … } blocks, @expr, @(expr), @Method(args), control flow, and code-only lines.</summary>
    private static string WithoutCode(string text)
    {
        // Balanced removal of @{ ... }, @( ... ) and @Name.Member( ... ) / @Name.Member[ ... ] / @Name.Member.
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '@' && i + 1 < text.Length && text[i + 1] == '@') { sb.Append('@'); i++; continue; }
            if (text[i] != '@') { sb.Append(text[i]); continue; }
            if (i + 1 < text.Length && text[i + 1] == '{') { i = PastClosingBrace(text, i + 1) - 1; sb.Append(' '); continue; }
            var j = i + 1;
            while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] is '_' or '.' or '!' or '?')) j++;
            // Control flow ("@if (…)", "@foreach (…)") has a space before its condition.
            if (text[(i + 1)..j] is "if" or "foreach" or "for" or "while" or "switch" or "using" or "lock")
                while (j < text.Length && text[j] == ' ') j++;
            while (j < text.Length && text[j] is '(' or '[')
            {
                var open = text[j];
                var close = open == '(' ? ')' : ']';
                var depth = 0;
                for (; j < text.Length; j++)
                {
                    if (text[j] == open) depth++;
                    else if (text[j] == close && --depth == 0) { j++; break; }
                }
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] is '_' or '.' or '!' or '?')) j++;
            }
            i = j - 1;
            sb.Append(' ');
        }
        // Lines that are C# statements inside markup (control flow, local variables).
        var kept = sb.ToString().Split('\n').Where(line =>
        {
            var t = line.Trim();
            return t.Length > 0
                && !(t.EndsWith(';') && !t.Contains('<')) // a C# statement inside markup ("x = …;")
                && !t.StartsWith("var ")                  // a local variable, even one that compares with "<"
                && !(t.Contains("=>") && !t.Contains('<'))  // a switch arm or lambda, not a tag with a handler
                && !t.StartsWith("else") && !t.StartsWith("} else") && !t.StartsWith("case ") && !t.StartsWith("default:")
                && t is not "{" and not "}" and not "};";
        });
        return string.Join('\n', kept);
    }

    /// <summary>
    /// The markup of a .razor file with every bit of C# taken out first, so a comparison like "x > 0" or a generic like
    /// "ILogger&lt;T&gt;" can't be mistaken for tags around text.
    /// </summary>
    private static string MarkupOnly(string razor)
    {
        var s = RazorComment().Replace(razor, " ");
        s = HtmlComment().Replace(s, " ");
        var codeAt = s.IndexOf("\n@code", StringComparison.Ordinal);
        if (codeAt >= 0) s = s[..codeAt];
        s = Directive().Replace(s, " ");
        s = WithoutCode(s);
        s = Svg().Replace(s, " ");
        s = StyleOrScript().Replace(s, " ");
        return s;
    }

    [Theory]
    [MemberData(nameof(InScopeFiles))]
    public void InScopeScreensHaveNoFixedEnglish(string relativePath)
    {
        var path = Path.Combine(Root, "src", "DigiByte.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var markup = MarkupOnly(File.ReadAllText(path));

        var pieces = TextNode().Matches(markup).Select(m => m.Groups[1].Value)
            .Concat(TextAttribute().Matches(markup).Select(m => m.Groups[1].Value));
        var leftovers = pieces
            .Select(WithoutCode)
            .Select(t => string.Join(' ', Word().Matches(t).Select(m => m.Value).Where(w => !AllowedWords.Contains(w))))
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        Assert.True(leftovers.Count == 0, $"{relativePath} still shows fixed text: {string.Join(" | ", leftovers)}");
    }
}
