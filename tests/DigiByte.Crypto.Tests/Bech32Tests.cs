using DigiByte.Crypto.Networks;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace DigiByte.Crypto.Tests;

public class Bech32Tests
{
    [Fact]
    public void RegtestAddress_MatchesNodeFormat()
    {
        // This witness program produces dgbrt1qxj9djv5u8zzvaftyj87sa8znu40gp63l5f0dmp on the node
        var witnessProgram = Encoders.Hex.DecodeData("348ad9329c3884cea56491fd0e9c53e55e80ea3f");

        // Encode with NBitcoin's bech32 using "dgbrt" HRP
        var encoder = Encoders.Bech32("dgbrt");
        var encoded = encoder.Encode(0, witnessProgram);

        // The node generates: dgbrt1qxj9djv5u8zzvaftyj87sa8znu40gp63l5f0dmp
        var nodeAddress = "dgbrt1qxj9djv5u8zzvaftyj87sa8znu40gp63l5f0dmp";

        Console.WriteLine($"NBitcoin: {encoded}");
        Console.WriteLine($"Node:     {nodeAddress}");
        Console.WriteLine($"Match:    {encoded == nodeAddress}");

        // If they don't match, let's see what the difference is
        if (encoded != nodeAddress)
        {
            Console.WriteLine($"NBitcoin length: {encoded.Length}, Node length: {nodeAddress.Length}");
            for (int i = 0; i < Math.Min(encoded.Length, nodeAddress.Length); i++)
            {
                if (encoded[i] != nodeAddress[i])
                    Console.WriteLine($"  Diff at position {i}: NBitcoin='{encoded[i]}' Node='{nodeAddress[i]}'");
            }
        }

        Assert.Equal(nodeAddress, encoded);
    }

    [Fact]
    public void RegtestNetwork_GeneratesValidAddress()
    {
        var network = DigiByteNetwork.Regtest;
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

        Console.WriteLine($"Generated address: {address}");
        Assert.StartsWith("dgbrt1", address.ToString());
    }
}
