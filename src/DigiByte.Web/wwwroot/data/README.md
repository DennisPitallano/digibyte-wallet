# common-passwords.txt

Deny list for passphrase setup (specs/009-stronger-pin-protection, research R6). One lower-cased, NFKC-normalised
entry per line, sorted. It is loaded lazily by `CommonPasswordList` and cached by the service worker, so it works offline.

## Contents

- **2,268 entries**, 33 KB. Only entries of 12 or more characters are included: shorter passphrases are rejected by the
  length rule before the deny list is consulted, so shorter entries would only add size.
- Built from three published lists in SecLists `Passwords/Common-Credentials/`, taking each list's most frequent
  qualifying entries in turn: `Pwdb_top-100000.txt`, `xato-net-10-million-passwords-100000.txt` and
  `100k-most-used-passwords-NCSC.txt`. Entries beginning `$HEX[` (hashcat-encoded) and any with control characters were
  dropped. A short hand-written set of well-known weak long passphrases was merged in.
- The size is smaller than the roughly 10,000 first planned because the 100,000-entry lists contain only about 2,000
  distinct entries of 12 or more characters. Reaching 10,000 would need the 1,000,000-entry list (about 8.7 MB source).

## Source and licence

- SecLists by Daniel Miessler, https://github.com/danielmiessler/SecLists, commit
  `dbdbd9ae1135fc53ecb0a9880af94178d0fc6264` (2026-11-08), MIT licence. The licence text is in
  `common-passwords-LICENSE.txt` and must travel with this file.
- The lists are used as data only. It is a deny list, not a strength meter: a passphrase not on it is not thereby strong.
