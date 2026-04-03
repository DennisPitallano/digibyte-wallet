using NBitcoin;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>
/// BIP39 mnemonic generation and recovery for DigiByte wallets.
/// </summary>
public static class MnemonicGenerator
{
    /// <summary>
    /// Generates a new 24-word BIP39 mnemonic.
    /// </summary>
    public static Mnemonic Generate(Wordlist? wordlist = null, WordCount wordCount = WordCount.TwentyFour)
    {
        return new Mnemonic(wordlist ?? Wordlist.English, wordCount);
    }

    /// <summary>
    /// Recovers a mnemonic from existing words.
    /// </summary>
    public static Mnemonic FromWords(string words, Wordlist? wordlist = null)
    {
        return new Mnemonic(words, wordlist ?? Wordlist.English);
    }

    /// <summary>
    /// Validates that a mnemonic phrase is correctly formed.
    /// </summary>
    public static bool IsValid(string words, Wordlist? wordlist = null)
    {
        try
        {
            var mnemonic = new Mnemonic(words, wordlist ?? Wordlist.English);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
