using DigiByte.Crypto.Headers;
using NBitcoin;

namespace DigiByte.Crypto.Tests.Headers;

public class MerkleProofTests
{
    private static (uint256 Txid, int Index, List<uint256> Branch, uint256 Root) Unpack(HeaderFixtures.MerkleVector v) =>
        (uint256.Parse(v.TxId), v.TxIndex, v.Branch.Select(uint256.Parse).ToList(), uint256.Parse(v.MerkleRoot));

    [Fact]
    public void Accepts_every_real_transaction_in_the_fixture()
    {
        Assert.True(HeaderFixtures.Merkle.Count >= 5);

        foreach (var v in HeaderFixtures.Merkle)
        {
            var (txid, index, branch, root) = Unpack(v);
            Assert.True(MerkleProof.Verify(txid, index, branch, root), $"block {v.Height} tx {v.TxId}");
        }
    }

    [Fact]
    public void The_fixture_transactions_hash_to_their_ids()
    {
        foreach (var v in HeaderFixtures.Merkle)
        {
            var raw = Convert.FromHexString(v.RawTx);
            Assert.Equal(v.TxId, MerkleProof.TransactionId(raw).ToString());
        }
    }

    [Fact]
    public void Rejects_a_proof_after_changing_one_byte_of_the_branch()
    {
        var v = HeaderFixtures.Merkle.First(x => x.Branch.Length > 0);
        var (txid, index, branch, root) = Unpack(v);
        var bytes = branch[0].ToBytes();
        bytes[0] ^= 0x01;
        branch[0] = new uint256(bytes);

        Assert.False(MerkleProof.Verify(txid, index, branch, root));
    }

    [Fact]
    public void Rejects_a_proof_with_a_different_index()
    {
        var v = HeaderFixtures.Merkle.First(x => x.TxCount > 2 && x.TxIndex == 0);
        var (txid, _, branch, root) = Unpack(v);

        Assert.False(MerkleProof.Verify(txid, 1, branch, root));
    }

    [Fact]
    public void Rejects_a_proof_for_a_different_transaction()
    {
        var v = HeaderFixtures.Merkle[0];
        var (txid, index, branch, root) = Unpack(v);
        var bytes = txid.ToBytes();
        bytes[31] ^= 0x80;

        Assert.False(MerkleProof.Verify(new uint256(bytes), index, branch, root));
    }

    [Fact]
    public void Rejects_a_branch_of_the_wrong_length()
    {
        var v = HeaderFixtures.Merkle.First(x => x.Branch.Length > 1);
        var (txid, index, branch, root) = Unpack(v);

        Assert.False(MerkleProof.Verify(txid, index, branch.Take(branch.Count - 1).ToList(), root));
        Assert.False(MerkleProof.Verify(txid, index, branch.Append(branch[0]).ToList(), root));
    }

    [Fact]
    public void Rejects_an_index_that_does_not_fit_the_branch()
    {
        var v = HeaderFixtures.Merkle[0];
        var (txid, _, branch, root) = Unpack(v);

        Assert.False(MerkleProof.Verify(txid, 1 << branch.Count, branch, root));
        Assert.False(MerkleProof.Verify(txid, -1, branch, root));
    }

    [Fact]
    public void Rejects_a_branch_that_is_too_deep()
    {
        var v = HeaderFixtures.Merkle[0];
        var (txid, index, _, root) = Unpack(v);
        var branch = Enumerable.Repeat(uint256.One, MerkleProof.MaxDepth + 1).ToList();

        Assert.False(MerkleProof.Verify(txid, index, branch, root));
    }

    [Fact]
    public void A_transaction_serialized_in_exactly_64_bytes_is_flagged()
    {
        // A 64-byte transaction can be confused with two joined merkle nodes, so callers must refuse it.
        Assert.True(MerkleProof.IsAmbiguousTransactionSize(new byte[64]));
        Assert.False(MerkleProof.IsAmbiguousTransactionSize(new byte[65]));
        Assert.False(MerkleProof.IsAmbiguousTransactionSize(new byte[63]));
    }

    private static List<uint256> Ids(int count) =>
        Enumerable.Range(1, count).Select(i => NBitcoin.Crypto.Hashes.DoubleSHA256(BitConverter.GetBytes(i))).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(33)]
    public void A_built_branch_verifies_for_every_position_in_trees_of_any_shape(int count)
    {
        var ids = Ids(count);
        var root = MerkleProof.Root(ids);

        for (var index = 0; index < count; index++)
        {
            var branch = MerkleProof.BuildBranch(ids, index);
            Assert.True(MerkleProof.Verify(ids[index], index, branch, root), $"{count} transactions, position {index}");
        }
    }

    [Fact]
    public void The_root_matches_the_one_in_real_blocks_for_the_fixture_branches()
    {
        // A one-transaction block has the transaction id as its root and an empty branch.
        var id = Ids(1)[0];

        Assert.Equal(id, MerkleProof.Root([id]));
        Assert.Empty(MerkleProof.BuildBranch([id], 0));
    }

    [Fact]
    public void A_branch_built_for_one_position_does_not_prove_another()
    {
        var ids = Ids(6);
        var root = MerkleProof.Root(ids);

        Assert.False(MerkleProof.Verify(ids[1], 2, MerkleProof.BuildBranch(ids, 1), root));
    }

    [Fact]
    public void Building_a_branch_for_a_position_outside_the_block_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleProof.BuildBranch(Ids(3), 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleProof.BuildBranch(Ids(3), -1));
        Assert.Throws<ArgumentException>(() => MerkleProof.Root([]));
    }
}
