using System.Collections.Concurrent;
using System.Security.Cryptography;
using DigiByte.P2P.Shared.Contracts;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Api.Hubs;

public class MultisigRoomHub : Hub<IMultisigRoomClient>, IMultisigRoomHub
{
    private static readonly ConcurrentDictionary<string, MultisigRoom> _rooms = new();
    private static readonly ConcurrentDictionary<string, string> _inviteCodes = new(); // inviteCode → roomId
    private static readonly ConcurrentDictionary<string, string> _connectionRooms = new(); // connectionId → roomId

    public async Task CreateRoom(string walletName, int requiredSignatures, int totalSigners, string initiatorName, string publicKeyHex)
    {
        if (string.IsNullOrWhiteSpace(walletName) || string.IsNullOrWhiteSpace(initiatorName) || string.IsNullOrWhiteSpace(publicKeyHex))
        {
            await Clients.Caller.Error("Wallet name, your name, and public key are required.");
            return;
        }

        if (totalSigners < 2 || totalSigners > 7 || requiredSignatures < 1 || requiredSignatures > totalSigners)
        {
            await Clients.Caller.Error("Invalid signature scheme. M must be 1–N, N must be 2–7.");
            return;
        }

        if (!IsValidPublicKeyHex(publicKeyHex))
        {
            await Clients.Caller.Error("Invalid public key format.");
            return;
        }

        var room = new MultisigRoom
        {
            WalletName = walletName,
            RequiredSignatures = requiredSignatures,
            TotalSigners = totalSigners,
            InviteCode = GenerateInviteCode()
        };

        room.Participants.Add(new MultisigParticipant
        {
            Name = initiatorName,
            PublicKeyHex = publicKeyHex,
            IsInitiator = true,
            ConnectionId = Context.ConnectionId
        });

        _rooms[room.RoomId] = room;
        _inviteCodes[room.InviteCode] = room.RoomId;
        _connectionRooms[Context.ConnectionId] = room.RoomId;

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
        await Clients.Caller.RoomUpdated(SanitizeRoom(room));

        // Schedule cleanup after expiry
        _ = Task.Delay(TimeSpan.FromMinutes(15)).ContinueWith(_ => CleanupRoom(room.RoomId, "Room expired."));
    }

    public async Task JoinRoom(string inviteCode, string name, string publicKeyHex)
    {
        if (string.IsNullOrWhiteSpace(inviteCode) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publicKeyHex))
        {
            await Clients.Caller.Error("Invite code, your name, and public key are required.");
            return;
        }

        var code = inviteCode.Trim().ToUpperInvariant();
        if (!_inviteCodes.TryGetValue(code, out var roomId) || !_rooms.TryGetValue(roomId, out var room))
        {
            await Clients.Caller.Error("Invalid or expired invite code.");
            return;
        }

        if (room.Status != MultisigRoomStatus.Waiting)
        {
            await Clients.Caller.Error("This room is no longer accepting participants.");
            return;
        }

        if (room.IsFull)
        {
            await Clients.Caller.Error($"Room is full ({room.TotalSigners}/{room.TotalSigners}).");
            return;
        }

        if (!IsValidPublicKeyHex(publicKeyHex))
        {
            await Clients.Caller.Error("Invalid public key format.");
            return;
        }

        if (room.Participants.Exists(p => p.PublicKeyHex == publicKeyHex))
        {
            await Clients.Caller.Error("This public key is already in the room.");
            return;
        }

        var participant = new MultisigParticipant
        {
            Name = name,
            PublicKeyHex = publicKeyHex,
            ConnectionId = Context.ConnectionId
        };

        room.Participants.Add(participant);
        _connectionRooms[Context.ConnectionId] = room.RoomId;

        if (room.IsFull)
            room.Status = MultisigRoomStatus.Full;

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomId);
        await Clients.Group(room.RoomId).RoomUpdated(SanitizeRoom(room));
        await Clients.OthersInGroup(room.RoomId).ParticipantJoined(SanitizeParticipant(participant));
    }

    public async Task LeaveRoom(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        var participant = room.Participants.Find(p => p.ConnectionId == Context.ConnectionId);
        if (participant == null) return;

        room.Participants.Remove(participant);
        _connectionRooms.TryRemove(Context.ConnectionId, out _);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        // If initiator left, close the room
        if (participant.IsInitiator)
        {
            await CleanupRoom(roomId, "Initiator left the room.");
            return;
        }

        // Reset room status if it was full
        if (room.Status == MultisigRoomStatus.Full)
            room.Status = MultisigRoomStatus.Waiting;

        // Reset ready states since participant count changed
        room.Participants.ForEach(p => p.IsReady = false);

        await Clients.Group(roomId).ParticipantLeft(participant.PublicKeyHex);
        await Clients.Group(roomId).RoomUpdated(SanitizeRoom(room));
    }

    public async Task SetReady(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        var participant = room.Participants.Find(p => p.ConnectionId == Context.ConnectionId);
        if (participant == null) return;

        participant.IsReady = true;
        await Clients.Group(roomId).RoomUpdated(SanitizeRoom(room));

        // Check if all participants are ready
        if (room.AllReady)
        {
            room.Status = MultisigRoomStatus.Creating;
            await Clients.Group(roomId).AllReady(SanitizeRoom(room));

            // Mark as created and cleanup after a brief delay
            room.Status = MultisigRoomStatus.Created;
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => CleanupRoom(roomId, "Wallet created."));
        }
    }

    public async Task SetNotReady(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        var participant = room.Participants.Find(p => p.ConnectionId == Context.ConnectionId);
        if (participant == null) return;

        participant.IsReady = false;
        await Clients.Group(roomId).RoomUpdated(SanitizeRoom(room));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionRooms.TryRemove(Context.ConnectionId, out var roomId))
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                var participant = room.Participants.Find(p => p.ConnectionId == Context.ConnectionId);
                if (participant != null)
                {
                    room.Participants.Remove(participant);

                    if (participant.IsInitiator)
                    {
                        await CleanupRoom(roomId, "Initiator disconnected.");
                    }
                    else
                    {
                        if (room.Status == MultisigRoomStatus.Full)
                            room.Status = MultisigRoomStatus.Waiting;

                        room.Participants.ForEach(p => p.IsReady = false);

                        await Clients.Group(roomId).ParticipantLeft(participant.PublicKeyHex);
                        await Clients.Group(roomId).RoomUpdated(SanitizeRoom(room));
                    }
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task CleanupRoom(string roomId, string reason)
    {
        if (_rooms.TryRemove(roomId, out var room))
        {
            _inviteCodes.TryRemove(room.InviteCode, out _);
            foreach (var p in room.Participants)
                _connectionRooms.TryRemove(p.ConnectionId, out _);

            try
            {
                await Clients.Group(roomId).RoomClosed(reason);
            }
            catch { /* clients may already be disconnected */ }
        }
    }

    /// <summary>Strip ConnectionId before sending to clients (server-only field).</summary>
    private static MultisigRoom SanitizeRoom(MultisigRoom room)
    {
        return new MultisigRoom
        {
            RoomId = room.RoomId,
            InviteCode = room.InviteCode,
            WalletName = room.WalletName,
            RequiredSignatures = room.RequiredSignatures,
            TotalSigners = room.TotalSigners,
            Participants = room.Participants.Select(SanitizeParticipant).ToList(),
            Status = room.Status,
            CreatedAt = room.CreatedAt,
            ExpiresAt = room.ExpiresAt
        };
    }

    private static MultisigParticipant SanitizeParticipant(MultisigParticipant p)
    {
        return new MultisigParticipant
        {
            Name = p.Name,
            PublicKeyHex = p.PublicKeyHex,
            IsInitiator = p.IsInitiator,
            IsReady = p.IsReady,
            JoinedAt = p.JoinedAt,
            ConnectionId = "" // strip server-only field
        };
    }

    private static bool IsValidPublicKeyHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        // Compressed public key: 33 bytes = 66 hex chars, starts with 02 or 03
        if (hex.Length != 66) return false;
        if (!hex.StartsWith("02") && !hex.StartsWith("03")) return false;
        return hex.All(c => Uri.IsHexDigit(c));
    }

    private static string GenerateInviteCode()
    {
        // 6-char alphanumeric, uppercase (e.g. "X7K9M2")
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I to avoid confusion
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(6, bytes.ToArray(), (span, b) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = chars[b[i] % chars.Length];
        });
    }
}
