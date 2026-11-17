namespace DigiByte.Wallet.Services;

/// <summary>One plain-language sentence for an M-of-N scheme (specs/018-multisig-ux-polish, FR-009).</summary>
public static class SchemeExplainer
{
    public static string Describe(int required, int total)
    {
        if (total < 1 || required < 1 || required > total) return "";
        if (required == total) return $"All {total} co-signers must approve each payment.";
        if (required == 1) return $"Any 1 of {total} co-signers can approve a payment on their own.";
        return $"Any {required} of {total} co-signers must approve each payment.";
    }
}
