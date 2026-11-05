using NBitcoin;

namespace DigiByte.Crypto.Psbt;

/// <summary>
/// Signs chosen inputs one at a time and tests completeness. Never uses <c>PSBT.SignWithKeys</c>, which signs any input the
/// key can satisfy, multisig inputs included, and ignores the requested signature type (research R1, R2).
/// </summary>
public static class PsbtSigner
{
    /// <summary>Signs a copy of the PSBT, only the listed inputs, with the standard "sign everything" type. The original is unchanged.</summary>
    public static PSBT SignInputs(PSBT psbt, IReadOnlyDictionary<int, Key> keysByIndex)
    {
        var copy = psbt.Clone();
        copy.Settings.SigningOptions = new SigningOptions(SigHash.All);
        foreach (var (index, key) in keysByIndex)
        {
            if (index < 0 || index >= copy.Inputs.Count) continue;
            var input = copy.Inputs[index];
            if (input.IsFinalized()) continue;
            input.Sign(key);
        }
        return copy;
    }

    /// <summary>
    /// Finalizes a copy and extracts the transaction if every script validates. The original stays unfinalized, so it can
    /// still be exported, combined, or finalized by another tool (research R3).
    /// </summary>
    public static PsbtCompletion TryComplete(PSBT psbt)
    {
        var copy = psbt.Clone();
        if (!copy.TryFinalize(out var errors))
            return new PsbtCompletion(false, null, errors.Select(e => e.ToString()).ToList());

        try
        {
            return new PsbtCompletion(true, copy.ExtractTransaction(), []);
        }
        catch (Exception ex)
        {
            return new PsbtCompletion(false, null, [ex.Message]);
        }
    }
}
