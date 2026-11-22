# Changelog

All notable changes to the DigiByte Wallet project.

## [Unreleased]

## [0.8.0] - 2026-11-22

A new menu, one Scan for every kind of code, and a first-payment flow for new wallets, with one shared look across the app. Where things are now: **Digi-ID** and **Settings** are under **More**, **Pay** is called **Request**, and the last step of a send is a slide. No stored wallet data needs upgrading.

### Added
- **Your address on an empty Home** — A wallet with no transactions now shows its receiving address, a QR code, **Copy address** and a link to Receive where the transaction list will be, so the first thing to do is on the first screen. It shows without waiting for the network, including offline, and stays available under "Unable to load data".
- **Your first payment is celebrated** — The first payment a new wallet receives shows a one-time celebration with the amount, on Home, Receive or Activity, whichever shows it first. It says "on its way" while the payment is unconfirmed, hides the amount when balances are hidden, and offers **Install app** when the wallet isn't installed. A recovered, imported or watch-only wallet that already has history never shows it. Nothing about it leaves the device.
- **Receive checks for payments while it is open** — The Receive page now notices an incoming payment by itself, at the same pace as Home (every 15 seconds, slowing to once a minute while nothing changes, paused while the tab is hidden), and shows the same "payment received" notice. A check that can't reach the network changes nothing on the page. Security-relevant: Receive now makes the same explorer requests Home already makes while it is open; no new kind of request and no new destination.
- **Receive button on an empty Activity** — "No transactions yet" on Activity now has a **Receive** button.
- **Optional sounds** — A new **Sounds** switch in Settings (off by default) plays a short chime when a payment arrives, including the first-payment celebration, and a different one when a send goes out. The notice or overlay still shows either way, so sound is never the only signal. The two audio files ship with the wallet (no new destination, no change to the content policy) and are cached for offline use. A browser that refuses to play, for example before the page has been touched, is ignored.

### Changed
- **A refreshed look** — Buttons, text fields, cards and QR codes across the wallet now share one style, with the same hover, pressed, focus and disabled behaviour on every page (see `docs/UI_STYLE.md`). Text fields are 16px, which stops iPhones zooming the page when a field is tapped, and have a darker border that is easier to see. QR codes sit on a white frame in both themes. The first-payment and send-success screens are a single card with the amount as the largest thing on it; the send screen shows every digit that was sent (it used to round to four decimals).
- **A new menu: Wallet, Activity, Scan, Request, More** — The menu is a floating bar (a side rail from 1024px wide) with a round **Scan** button in the middle. **Pay** is now **Request**, and its page is titled "Request a payment". **Digi-ID** and **Settings** moved into **More**, together with Multisig, Contacts and the other tools; a one-time tip above More tells returning users where they went. Home's own Scan and More buttons are gone, since the menu is always on screen; Multisig takes their place beside Send, Receive and Contacts. The menu, the More list and the scan messages are in English and Spanish.
- **One Scan for every kind of code** — The app-wide Scan opens Send for a DigiByte address or payment request, Digi-ID for a login code, the right multisig for a co-signer's proposal (it is added to Pending there), Import multisig for a redeem script, and the room for a multisig room invite. Security-relevant: a code that is none of these opens nothing and says so (before, Home's scanner put whatever it read into Send's address field); a web link is followed only when it is this wallet's own room invite on this wallet's own address; a proposal or redeem script is handed over in memory and never put in an address.
- **Help matches the new menu** — A new **Getting Around** section explains the five menu buttons, what Scan reads, what is in More, sounds and slide to send, and every answer that said "Pay tab", "ID tab" or "Go to Settings" now gives the current path.
- **Donate opens the right page** — Donate in the More list led to an address with no page behind it. It now opens the donations page.
- **Slide to confirm when coins leave the wallet** — The final step of Send, DigiDollar send, Consolidate, Digi-ID fund recovery and a multisig broadcast is now a slide control instead of a button, so it cannot be confirmed by a stray tap. Security-relevant: only the control changed; what is signed and broadcast, and every check before it, are the same. A failed attempt leaves the control unlocked for another try.
- **Recovering word by word is easier** — Each word of the recovery phrase is one box with its number inside, in two or three columns that fit the screen (the old three-column grid ran off the side on narrower screens). A recognised word gets a tick, an unrecognised one a red border, and a line above the grid counts the words entered and how many are not recognised. The text is 16px, so iPhones no longer zoom the page when a box is tapped, and the 12 or 24 choice is a proper switch. Pasting the whole phrase into box 1 still fills every box.
- **No more zooming when you tap a field on a phone** — iPhones zoom the whole page when a tapped field has small text, and do not zoom back. About 35 fields had 12px or 14px text (amounts, search boxes, labels, the dropdowns in Settings). On touch screens every text field is now 16px; larger ones such as the PIN boxes are unchanged, and mouse-driven screens keep the compact sizes.
- **Readable grey text in the light theme** — Labels, hints and section headings in light grey were about 2.6:1 against white, well under the 4.5:1 that small text needs. In the light theme they are now a darker grey (about 5.3:1). The dark theme is unchanged.
- **Consistent headings, tabs, choices and icon buttons** — Page titles, card and dialog titles and the small section labels each have one style (there were 44 hand-written heading variants). Tabs such as Recovery phrase / Private key are one control with a raised selected tile. Choice buttons such as fee speed and the All / Received / Sent filter share one look, filled when selected. The "+" beside a page title and other square icon buttons are 44px, with hover, pressed and focus states. Selected tabs and choices are now announced to screen readers, and two icon buttons that had no spoken name got one.
- **The balance card is a wallet you can snap shut** — The balance on Home is drawn as a closed wallet in the wallet's own colour: a stitched body, a strap across the right edge, and a metal snap. Pressing the snap locks the app straight away, so the PIN or passphrase is needed again; until now the only ways to lock were the timer and leaving the tab. The DigiByte mark is pressed into the skin near the bottom edge, like a maker's stamp, and the wallet's name and address sit beside it. The default finish is smooth leather; pebbled leather, cotton, plastic and metal in Manage Wallets are unchanged, and the wallet looks the same in the light and dark themes.
- **One back button everywhere** — The back control at the top of every page is now the same: a chevron and label in a 44px-high tap target with hover, pressed and keyboard-focus states. It used to be written five different ways, most of them a small text link that was hard to hit with a finger. "Back" steps inside multi-step forms use the shared quiet button style.
- **A quieter first session** — The install banner and the "Supported by" sponsors strip now wait until a wallet on the device has a transaction. The setup pages and **Install app** in Settings are unchanged, and existing users see no difference.
- **An unreachable explorer is no longer read as an empty history** — When address summaries could not be read, the wallet fell back to a reader that answers "no transactions" for an address it cannot reach, and reported the result as complete. It is now reported as incomplete: Activity keeps the saved list with "Couldn't refresh" instead of showing "No transactions yet". Each history load also reports its own completeness, so two loads running at once can no longer be mistaken for each other.
- **An empty Home can scroll** — Home stops the page from scrolling so the transaction list can scroll instead; with no list, the lower part of the page could not be reached on a small screen.
- **Even fewer requests while the wallet is open** — An automatic refresh now checks only the addresses that can change: ones with funds or pending payments, the address on show and the last few handed out, the next change address and anything used in the last week. The others are checked at least every 5 minutes, a few at a time. A quiet refresh drops from about 60 requests to about 10, so the explorer's rate limits are hit even less often. A payment to an old, unused address shows within 5 minutes; pulling down or tapping refresh checks every address at once. Home's wallet info panel shows when every address was last checked.
- **Quicker sends** — Confirming a send now reads the coins of the addresses that hold money (2 or 3 for most wallets) instead of every address, still fresh at that moment. If they aren't enough, the wallet reads the rest before saying funds are insufficient.
- **Developer switches stay on developer devices** — The `/dev/node` switches now count only while the developer view is on, so a switch left off on a device can never change a regular user's wallet.

### Removed
- **The five introduction slides** — The first Home no longer opens under a slideshow. The address to share and the getting-started checklist take its place.

### Security
- **Scan opens only what it recognises** — A scanned code that is not a DigiByte address, a payment request, a Digi-ID login or a multisig code opens nothing. Before, Home's scanner put whatever it read into Send's address field. A web link is followed only when it is this wallet's own multisig room invite, on this wallet's own address.
- **Slide to confirm and lock on demand** — Only the controls changed: every send, sweep and broadcast runs the same checks and signs the same way as before, and the snap on the balance card locks the app exactly as the auto-lock does.
- No key, seed or password handling changed for the first-receive flow. It stores two non-secret values on the device: whether each wallet started empty, and whether the device has had a transaction.

## [0.7.0] - 2026-11-21

Multisig co-signers can now sign together online, balances and history load much faster, and Home shows pending payments. No stored wallet data needs upgrading: the wallet starts saving address summaries on the device the first time it refreshes.

### Added
- **Sign a multisig payment together online** — On a pending payment, **Sign together online** opens a room for 15, 30, 60 or 120 minutes (default 30) with a 6-character code, a link and a QR code. Co-signers join from the link or with **Join a room with a code** on the Pending page; a locked wallet unlocks first and comes back with the code filled in. Each wallet reads the recipient, amount, fee and note from the transaction itself, checks it against its own copy of the multisig, and signs on the device. Once there are enough signatures, one participant broadcasts and everyone sees the transaction ID. Dropped connections rejoin by themselves, nothing collected is lost when a room ends, and if the server is down **Share** and **Import** work as before. Security-relevant: a new coordination relay (`/hubs/multisig-signing`). It holds rooms in memory only, never sees a key, checks every signature against what it signs, needs a single-use proof that the joiner holds one of the multisig's keys (a wrong code, a bad proof and a non-co-signer all get the same reply), and is rate limited.
- **Receive a co-signer's payment proposal by QR scan** — On a multisig's Pending page (or the new **Scan** button on its dashboard), use the **Receive proposal** options (**Scan QR**, **Paste**, **Choose file**) and scan the QR code another co-signer is showing, or paste it, or choose a file. The payment is added to your pending list, labelled "Received from a co-signer", with the recipient, amount and fee read from the transaction itself. Scanning the same proposal again never makes a duplicate and merges any new signatures. A proposal for another multisig of yours offers to open that multisig; one you cancelled can be restored after you confirm. A proposal whose coins were already spent elsewhere shows "Can't be signed – coins already spent". Nothing is sent to a server. Security-relevant: a new way to bring signed-transaction data into the wallet; copies signed by a key that is not a co-signer are refused whole.
- **Pending payments on Home** — A payment on its way to you, or a send of yours that hasn't confirmed yet, now shows on its own line under the balance ("Pending +X DGB" or "Pending −X DGB"). The main figure and its fiat value are your confirmed balance only, so a pending amount that is dropped never moves it. Send's **Available** amount no longer offers coins that a pending send already spent.
- **Wallet pages open with your figures** — Activity, Receive and Send now show the last known balance and history at once and update in the background, as Home already did. If the network can't be reached they keep what was shown with "Last updated", instead of a partial list.
- **Remove cancelled multisig proposals** — Each cancelled proposal on the Pending list has **Remove from list**, and **Clear all cancelled** (after a confirmation) appears when there is more than one. Only cancelled proposals can be removed, only on this device, and they leave Activity too; nothing on the chain changes.
- **Pull down to refresh a multisig** — Pulling down on a multisig's page reloads its balance and recent activity. A failed refresh keeps what was shown and says it couldn't refresh.
- **Install app in Settings** — A new **Install app** entry, shown while the wallet isn't installed. On Android and desktop Chrome or Edge it opens the browser's install prompt; on iOS it shows the Add to Home Screen steps; otherwise it explains how to install from the browser menu.
- **More Latin American currencies** — 14 new display currencies with flags: Dominican peso, Colombian, Argentine, Chilean and Uruguayan pesos, Peruvian sol, Bolivian boliviano, Paraguayan guaraní, Venezuelan bolívar, Costa Rican colón, Guatemalan quetzal, Honduran lempira, Nicaraguan córdoba and Cuban peso. Search finds them by country name.
- **Change-only history switch and refresh benchmark** (developer view, `/dev/node`) — A switch for the new change-only sync (on by default, kept per device), and a benchmark that times a wallet refresh with it on and off and counts the explorer requests by kind. Counts stay on the page.
- **Explorer vs gateway balance benchmark** (developer view, `/dev/node`) — Times reading the balance of a pasted address list from the explorer and from the light-client gateway, and shows whether the balances match.

### Changed
- **Clearer multisig creation and signing** — Signing rooms show each co-signer as a card (You or Host, online, waiting, signed or declined) with one headline and at most one main button, and **Review & sign** restates the amount and recipient before you sign. The room and the Pending list share one payment summary. Creating a multisig has a step indicator, signer cards and the invite code with a link and QR code. No signing, storage or server rule changed.
- **Data source window** — Redesigned with a status chip that follows the data-source state, one line per point, and the privacy note in its own panel.
- **Faster balance and history** — A refresh with no new activity now reads one summary per address and nothing else, instead of a balance read, a history read and a block-height read per address. In a live test on a 60-address wallet, a refresh took 316 ms and 60 requests against 1,163 ms and 120 with the old way, so the explorer's rate limits (and "Unable to load") are hit less often. History is fetched only for addresses that changed and saved on the device like Home's cached figures; once per unlock the wallet re-reads everything in the background to catch rare chain changes. Pages asking for different amounts of history share one fetch, and identical reads made at the same moment share one request. A failed lookup still fails the balance, so it is never shown too low, and the coins for a send are always read fresh.
- **Light-client gateway history in one pass** — With the gateway answering, a wallet's history is built in one pass over its record, reading each transaction once instead of once per address.

### Fixed
- **Busy addresses show their whole history** — An address with more than 25 transactions (for example a reused donation address) showed only the newest 25. It now shows all of them, multisig addresses included.
- **The balance refreshes right after a send** — Send, PSBT signing and Settings cleared a cache key Home never used, so the balance saved before a send could stay on screen. They now clear the wallet's own saved figures.
- **Deleting a wallet removes all its data** — Its tracked transactions, saved chain history and the figures pages show at once are now removed with it.
- **A balance never reads too low when some lookups fail** — A rate limit or timeout on a few of a wallet's addresses counted them as empty, so the balance could show too low with nothing to tell it apart. Now any address that can't be read fails the whole read, the next source is tried, and the screen says "Unable to load" instead of a wrong total.
- **Stuck splash or bare "Offline" page on load** — On Android Chrome the app could stop at about 70% with "An unhandled error has occurred", or show a black page that only said "Offline". The service worker no longer reloads the page on a first visit, a half-filled cache no longer replaces a working one, and a failed load shows a recovery page with **Try again** and **Repair app files**.
- **"You" in a multisig shared by two of your wallets** — The creator's key showed as "You" in every wallet, so the second wallet saw the wrong signature status and Sign button. Each wallet now sees its own key as "You".
- **Multisig Send's QR scanner** covers the whole screen again instead of appearing behind the form.
- **Proposal options always visible** — The Pending page now shows **Scan QR**, **Paste** and **Choose file** for a co-signer's proposal straight away, instead of behind the camera.
- **Switching wallets on the signing-room join page** with no code typed no longer tries to join and shows an error; it asks for the 6-character code first.

### Security
- The multisig creation room now needs proof that the caller holds the co-signer key they present, using the same single-use proof as the new signing rooms. Before, any well-formed public key was accepted.
- The new signing relay is described under Added. No key, seed or password handling changed in this release.

## [0.6.0] - 2026-11-17

This is the first release since 0.4.0 that is published as a tag: version 0.5.0 below (stronger PIN protection) was never tagged, so everything in 0.5.0 ships here too. Existing wallets upgrade their PIN protection the first time they are unlocked.

### Added
- **Multisigs belong to the wallet that made them** — A multisig and its drafts now show only under the wallet that created or imported it (or holds one of its keys), so switching wallets no longer shows another wallet's multisigs. Multisigs saved before this change are matched to a wallet by their co-signer keys; any that no wallet can claim appear under **Unassigned multisigs** with a **Claim** button. Removing a multisig removes it only from the active wallet; if another wallet also owns it, it stays there. Nothing is deleted by the upgrade. Security-relevant: changes how multisigs are stored and shown.
- **Rename a multisig, a draft or a co-signer** — Tap the name at the top of a multisig (or the pencil in the list) to rename it, and tap a co-signer to edit their label. Names are 1 to 40 characters, local to your device, and never change the address, the redeem script or any key.
- **See and check the full co-signer keys** — Tap a co-signer to see their whole public key in readable groups, with **Copy** and a QR code, so you can compare it with what they sent you.
- **Compact multisig dashboard** — A balance card like the wallet's own (hide, refresh, fiat value, address with copy and QR), **Send / Receive / Pending / Activity** buttons, a "needs your signature" banner, and folded sections for the redeem script and backup, co-signers and recent activity. The backup warning stays until you download the backup or copy the script, then shows the date.
- **Send from a multisig like Send DGB** — Paste, scan or pick a contact, **Available** with 25 / 50 / 75 / Max, a fee and "left in multisig" preview, a review card with the full address in groups of four, and a duplicate-tap guard. The fee now comes from the real size of the transaction, and change too small to send back (under 0.001 DGB) goes to the fee. After creating a proposal you can **Sign Now** or copy it to share.
- **Multisig Activity** — Payments received, payments sent and proposals in one list, newest first, with All / Received / Sent / Pending / Failed filters, the first 25 entries then **Load more**, and a detail view with the full transaction ID, copy and an explorer link. When the network is unreachable it still shows your proposals and says so.
- **Better pending multisig payments** — A signer checklist ("1 of 2: You ✓, Treasury —"), Sign, Share (copy, QR or file), Import signature (paste, scan or file), Broadcast, Retry after a failed broadcast, and Cancel. Cancel only affects this device: co-signers who already have a copy could still broadcast it, and the confirmation says so. Imported signatures are checked to belong to that proposal and to come only from the multisig's own keys. Security-relevant: new signature-import checks.
- **Redeem script QR code** — The multisig's redeem script and backup panel can show the script as a QR code (and save it as a PNG), and **Import** has a **Scan QR** button that fills in the script. The code holds every co-signer's key, so treat it like the backup file.
- **Your addresses on the Receive page** — HD wallets get a folded **Your addresses** list under the QR code: the newest 10 receiving addresses (10 more at a time), each marked Used (with a transaction count), Unused or Status unknown. Pick one to show its QR code, copy, share and download; name an address with the pencil; open **View activity** for a used one. A note says when the address on show has been used before, because a new address is better for privacy.
- **Your own address on each activity row, and an address filter** — Activity rows show which of your addresses a transaction paid or came from (with its label), and the Activity page has an address picker that narrows the list to one address. The chosen address is remembered per wallet and addresses can be named from the picker. Search also matches your addresses and labels.
- **Oracle price and DigiDollar status** — The DigiDollar home card and Analytics show the oracle price, how fresh it is and the protection state again, read through the project's data service from digibyte.io. Display only: it is never used for a transaction, the browser never contacts digibyte.io, and a failed fetch is retried on its own up to three times.
- **Public Network status page** (`/network`) — A read-only page that explains, in plain words, which data sources the wallet is using, whether each is ready and how it has answered this session. Deployments can hide it with `OwnNode:PublicStatus=false`.
- **Light-client gateway tools** — The gateway has an operator status page (block-index progress, peers, a live request list with no addresses or callers, a test request, a phone layout and a token-protected reset), indexes blocks much faster, and the web server can reach it over a private `/spv` route with a built-in speed comparison of the two routes.
- **Wallet card finishes** — Choose smooth, leather, cotton, plastic or metal for each wallet's balance card when you pick its colour in Manage Wallets.
- **Home and Settings polish** — Hide-balance toggle, 24-hour price change with a 7-day chart, a **Request a specific amount** panel on Receive (the QR code and link carry the amount), a getting-started checklist, a shared page header with a Back link, an Activity tab with a monthly summary, JetBrains Mono numbers, grouped Settings and an Appearance setting (System / Light / Dark).
- **DigiDollar switches without a rebuild** — `WALLET_DD_VIEW`, `WALLET_DD_SEND` and `WALLET_DD_ORACLE` change the DigiDollar switches at the next restart.

- **Copy your multisig public key** — Settings has a new **Copy my public key** card, and the multisig create screen has a **Copy** button next to your key. Both name the wallet the key belongs to, since each wallet has its own key.
- **Backup prompt after creating a multisig** — Creating a multisig now ends on a "Wallet created" screen with **Download backup file** and **Copy redeem script**, instead of jumping to the wallet list. The backup file also explains how to restore without it (restore each seed, then create again with the same threshold and public keys).

- **Save a multisig as a draft** — If your co-signers aren't available yet, tap **Save draft and finish later** on the co-signers step. The draft (name, threshold and any keys entered so far) appears under "Drafts" on the Multisig page, and **Continue** picks up where you left off; it is removed once the wallet is created. Drafts hold only public information, and the app warns if a draft is resumed from a different wallet than the one that started it.

- **P2P light-client gateway (off by default)** — A new service, `DigiByte.SpvGateway`, talks the DigiByte peer protocol, checks block headers and their proof of work, finds the blocks that involve a wallet's addresses, and answers over HTTP with merkle proofs. The wallet can use it as a backup data source: it matches each transaction to block headers the wallet checked itself, so a gateway cannot invent a payment. Turn it on with `SpvGateway:Enabled` and `SpvGateway:ApiUrl` (container variables `WALLET_GATEWAY_ENABLED` and `WALLET_GATEWAY_API_URL`); `SpvGateway:Position` is `backup` and a request for `first` is ignored until the wallet checks proof of work itself. Security-relevant: a new anonymous service that sees the addresses a wallet asks about, and a new origin in the wallet's Content Security Policy.
- **Checked, not yet verified** — Transactions the gateway proved show a **Checked** tag (matched to headers the wallet verified, at least 6 blocks deep); transactions that came from an explorer and are not yet proven show **Unchecked**; if the sources disagree the tag is **Disputed** and the amount is not spendable. The wallet does not yet check proof of work itself, so nothing is labelled **Verified**, and a checked transaction is not protection against a dishonest data source.
- **Your own change is spendable at once** — With the gateway, change from a payment you just sent can be spent before it has 6 confirmations; payments from others still wait for 6.
- **Restore through the gateway** — The recovery screen has an optional "when did you first use this wallet" date. With the gateway on, the restore reads only the blocks since then and finds funds across the address gap limit. If the history is older than the gateway keeps, the wallet says so and restores through the explorers as before.
- **Source status** — The data-source panel names the light-client gateway, says it sees the addresses the wallet asks about, and the Deployment page shows its state.

### Changed
- **DigiDollar transactions look right** — A DigiDollar transaction moves no DGB except its fee, so the list and details now show its kind, the DigiDollar amounts and the fee instead of "-0.0000 DGB" and a fake "DD" note, and offer no recipient actions. The DigiDollar section reads from the explorer again by default, because the node API it used no longer exists.
- **Data source order** — The offline Blockbook explorer (it answered every request with an error) is removed from the explorer list, the cross-check, the content policy and the docs. With node use off, reads and the spendable-output check now ask the light-client gateway, which has a readiness check and verified answers, before the unchecked node. The status cards follow the same order.
- **Easier co-signer entry** — Each co-signer key field has a **Paste** button and checks the key as you type: a green check when valid, a clear message when it is malformed, not compressed (66 characters starting with 02 or 03), or a duplicate of your key or another co-signer's. The create screen shows the step name and "N of M added".
- **Import finds your key for you** — Importing a multisig no longer asks for a "key position" number. The wallet looks for your key in the redeem script and says whether you can sign or the wallet will be watch-only.
- **Multisig guide and Help corrected** — They no longer say the public key is in settings or the receive page, and now explain how to create a wallet without everyone online.
- **Multisig screens match the wallet** — The multisig dashboard, Send, Receive, Pending, Activity, Create and Import now use the same cards, buttons, inputs and QR style as the rest of the wallet.
- **Bottom navigation** — Activity replaces the P2P placeholder; P2P moves to **More**.

### Fixed
- **Multisig co-signer names** update as you type, and a link from one multisig to another (or to a missing one) no longer shows the previous multisig's data.
- **Received-funds toast** appears once per incoming transaction, not on every refresh when an explorer drops some address lookups.
- **Oracle reading on the dashboard** is merged after the DigiDollar card is built, so it no longer sits on "Loading price..." until a manual refresh; the data-source dot is green when public explorers answer normally.
- **Light-client gateway** answers peer pings (peers used to drop it as inactive and the peer count drained away), keeps topping up peers during long block fills, and a slow or silent peer no longer stalls the sync.

### Security
- Multisig signature import now checks that a copy belongs to the proposal and is signed only by keys of that multisig; a cancelled proposal is never broadcast from the device that cancelled it, and concurrent broadcasts of one proposal broadcast once.
- Scanned and pasted text (addresses, payment links, signed transactions, redeem scripts) is classified and checked for the active network before it is used.
- No key, seed or password handling changed in this release apart from the PIN protection described under 0.5.0.

## [0.5.0] - 2026-11-09

> Not tagged on its own: these changes were published as part of 0.6.0.

### Added
- **Stronger PIN and passphrase protection** — The wallet now turns your PIN or passphrase into an encryption key with Argon2id, a method that needs a lot of memory for every guess, instead of PBKDF2 alone. The result is stored in a versioned `dgb2` envelope whose header is authenticated. Three cost profiles (standard 64 MiB, low-memory 32 MiB, floor 19 MiB) are tried in order so the wallet still unlocks on small devices. The cost values are provisional: they have not yet been measured on a low-end phone (T001), and each stored value carries its own parameters so they can be raised later without breaking existing wallets. Security-relevant: this changes how every stored secret is encrypted. Finding DGB-04 of the external review.
- **Passphrase option** — Choose a passphrase (at least 12 characters, normalised, checked against a list of common passwords) instead of a 6-digit PIN when you create or reset a wallet. A forgotten passphrase cannot be reset; only the recovery phrase restores access.
- **Automatic upgrade** — Existing wallets upgrade the first time they are unlocked, with the same PIN and no action needed. The upgrade is safe to interrupt. The old copy is kept for 7 days so an older version of the app can still unlock, then removed.
- **Biometric protection** — Where the browser supports it, the biometric key comes from the authenticator itself instead of being stored next to the data it protects. Existing biometric users keep working and are offered a one-time re-enrolment after their next PIN unlock. Where it is not supported, a plain warning explains that biometrics lowers protection against copied browser data.
- **Browsers that cannot run Argon2id** — A new wallet is created with the older protection only after an explicit agreement, is marked as such, and is upgraded when opened on a capable device.
- **Recover funds on the Digi-ID page** — A Digi-ID address is a real DigiByte address, but the wallet never watched it, so DGB sent to one by mistake was invisible. The ID page now has a **Recover funds** section for a wallet made from a seed phrase. It checks the Digi-ID address of each site in your login history, plus any website name you type in (useful after restoring a wallet, when the history is empty), and lists the confirmed and pending funds found. You review the amount, the network fee estimate and where the funds will arrive, confirm once, and each site's funds are moved into your wallet. Sweeps are saved and their status is shown (Sending, Pending, Confirmed, Failed) after a reload, and kept current while the section is open. Funds smaller than the fee, pending funds, and outputs that are not ordinary DGB are left alone and reported. Security-relevant: this is a new signing and broadcast path.
- **Privacy by design** — Each address is looked up in its own request, each site is swept in its own transaction, and each transaction pays to a different new address, so neither a block explorer nor the blockchain can tell that your sites belong to the same person. One confirmation covers up to 18 to 19 sites; the review says how many more can follow once the first round confirms.
- **Warning beside every Digi-ID address** — The signing address on the approval panel, on the success panel, in the login detail dialog and in Help now says it is an identity address, that the wallet does not watch it, and that it must never be used to receive payments, with a link to Recover funds. Available in English and Spanish; other languages show English. A new Help entry explains it, including that DigiDollar and other tokens must never be sent to a Digi-ID address.

### Security
- Closed several server-side holes found in an external review. Details follow once the fixes are deployed and the advisory is published.
- Removed Google Analytics (`gtag.js`) from the wallet; no third-party script runs in the wallet origin.
- The web server now revalidates scripts, styles and data files that keep their name across releases (such as `js/crypto.js`) instead of caching them for a year as immutable; only the fingerprinted `_framework/` files stay immutable. A stale `crypto.js` next to a new build would break unlock.
- The wallet's web server now sends frame, content-type and referrer headers on every route.
- `/api/health` on all three APIs reports the commit the running build came from (`commit`).
- Merchant registration and the Digi-ID callback proxy are rate limited per client.

### Changed
- The public directory no longer accepts new entries or deletions; lookups are unchanged.
- Digi-ID sign-in now requires an https site on port 443.
- Unlock, signing and reset messages say "PIN" or "passphrase" correctly in English and Spanish, and an environmental failure (such as low memory) is no longer counted as a wrong attempt.
- The unlock and PSBT-signing pages show a progress indicator while the key is derived, and ignore a second tap.
- App version bumped to 0.5.0.

## [0.4.0] - 2026-11-06

### Added
- **Sign a PSBT** (`/settings/psbt-sign`, Advanced mode) — Paste a PSBT as base64 or upload a `.psbt` file (up to 1 MB) made by another wallet or by DigiByte Core. The review shows the total leaving the wallet (or received), every output with its full address, the fee and whether it is verified, and how many inputs this wallet signs. Inputs count as the wallet's only when their script is one of its scanned addresses; their amounts are checked against the network before review and again before signing. Signing runs the acknowledgment for warnings, the spending limit, Baku's send-risk warning, then biometrics or a fresh PIN, and signs only this wallet's inputs with the standard signature type. The result is copied as base64 or downloaded as `.psbt`, and a complete transaction can be broadcast from the wallet. Missing coin data for the wallet's SegWit inputs is filled in from the network. Refused with a plain reason: DigiDollar transactions, Taproot inputs, non-standard signature types, false amounts, spent coins, and outputs above inputs. Watch-only wallets can review only. Other participants' amounts in shared PSBTs are not yet verified, and the review says so. Security-relevant.
- **Shared wrong-PIN delay** — The unlock screen's wrong-PIN delay is now a shared `PinAttemptThrottle`, also used by PSBT signing. Behaviour on the unlock screen is unchanged.
- **Sponsors page** (`/sponsors`) — Lists the people who support the wallet, read live from the project's own `sponsors` branch on GitHub (no database). Shows a picture, name and optional link for each sponsor, newest first, with a letter placeholder when a picture is missing, 48 at a time, and a clear "may be out of date" or retry state when GitHub cannot be reached. Reached from Settings, About, Donation History and the footer links on the Welcome and Unlock screens.
- **Sponsors highlighted** — A "Newest supporters" spotlight on the Sponsors page, a "Supported by" strip of the newest six on the Home screen (loads quietly in the background, reuses a saved copy for 30 minutes, hidden on any problem), and more prominent Sponsors links (heart and rose accent).
- **Mascot and donation info on the Sponsors page** — Baku says thank you (and follows the Baku setting), and a "Where your donations go" section explains what donations pay for (hosting, our own DigiByte node, maintenance and security, new features) and links to the public Donation page.
- **Add me to the list** — A donor with no GitHub account first sees the project's donation QR code and address, then fills in a form (name, optional link and picture, and the DGB transaction ID of their donation, plus a consent confirmation). `POST /api/sponsors/submissions` on the API checks the donation automatically against the public explorers (3 confirmations, at least 1 DGB to a project address, a donation can be used once; donating again under a listed name adds to that sponsor's "Donated N times" count), then a bot opens a pull request on the `sponsors` branch for a maintainer to review. Rate-limited (3 per visitor per hour), capped (50 open submissions) and size-limited. "Remove me" works with the same transaction ID. See `docs/SPONSORS.md`.
- **`sponsors` data branch** — Validation (`validate-sponsors`) and list-generation (`build-sponsors-index`) workflows, with scripts and fixture tests.
- **Constitution 1.1.0** — Allows a public-contribution intake relay as a server role, a path-scoped exception for the project's own raw GitHub content in the content security policy, and anonymous intake endpoints that can only create review items.
- **Railway full-node deployment** — New `docker/digibyted/digibyte-railway.conf` and `entrypoint-railway.sh` run an unpruned DigiByte Core v9.26.7 node with a transaction index and no wallet. RPC credentials and the allowed address range come from Railway variables, and the node refuses to start without them. Runbook in `docs/RAILWAY_NODE.md`.
- **Sync-aware `/api/health`** on the node API: `status` (`starting`, `syncing`, `ok`, `unhealthy`), node version, block and header height, lag, peers, transaction index state, and a version check against `DGB_RECOMMENDED_VERSION`. Returns 503 only when the node cannot be reached.
- **Operator route gate** — Outside Development the node API now requires `X-NodeApi-Key` for key, wallet, mining, PSBT, descriptor, node-control, block invalidate/reconsider, `tx/create`, `tx/fund` and costly or peer-revealing routes, and answers 503 when no key is configured.
- **Wallet can use the team's own DigiByte node when it is ready** (off by default) — A small status dot opens a plain-language detail of the active source. While the node is ready, sending, transaction lookups, fees and chain height try it first; address balances and spendable outputs stay explorer-first with the node as a backup, and address history always comes from explorers. The wallet uses the node only while its health report says ready and its height agrees with a public explorer, switches back to explorers by itself, and shows a banner only on real degradation. Settings (`WALLET_NODE_*`) are written to `config.json` when the web container starts, so they change without a rebuild. A developer-only page (`/dev/node`, switched on with `WALLET_DEV_VIEW`) compares the node with the explorers. See `docs/RAILWAY_NODE.md`.
- **Node client strict mode** — With node use on, a failed node request is an error (and falls back to an explorer) instead of a zero balance, empty list, default fee or height 0. Behaviour with node use off is unchanged.

### Changed
- The node image (`docker/digibyted/Dockerfile`) now defaults to DigiByte Core v9.26.7 and verifies the download against the release's `SHA256SUMS`.
- Production node API refuses to start with empty or `changeme` RPC credentials. The node API image no longer bakes in a password, testnet mode or the faucet (local compose files set these explicitly).
- `/api/health` no longer returns the node URL, and failed node connections no longer put the node's host and port in API error messages (details go to the server log).
- The DigiDollar fee-reserve monitor stays idle when no DigiDollar wallet name is configured, so a node without a wallet does not log a warning every minute.
- App version bumped to 0.4.0.

### Fixed
- **Change kept on scanned addresses** — Change addresses were scanned only up to index 9, so the 11th send in a session could put change on an address the wallet never checked, lowering the balance and failing the next send. Change is now scanned up to the next change index, which is saved after each send instead of restarting at 0.
- **Every receive address shown is scanned** — "Generate new address" could go past index 19, where payments were missing from the balance and couldn't be spent. Receive addresses are now scanned up to the one on show and the highest used, the index is saved, and each unlock searches the chain for used addresses. The Recovery page adds **Search for Missing Payments**, which looks 500 addresses further.
- **Address discovery fails safely** — A failed explorer lookup no longer counts as an unused address, sets no cooldown or outage state, and every address search stops after a limit instead of running forever against a source that reports history for any address. Balance, history and sends wait a limited time for discovery.
- **Batch sends recorded in full** — A batch send was saved with only the first recipient's amount, so history showed too little and spending limits undercounted it. It is now saved once with the total, and older records are repaired when history loads.
- **Recovery page text** — Three non-UTF-8 bytes garbled the page title and other text.

### Security
- Upgraded the SignalR client to 10.0.13 (GHSA-334f-vvp4-7g6p), and the server packages to versions with patched Microsoft.OpenApi 2.7.5 (GHSA-v5pm-xwqc-g5wc) and SQLite 3.53.3 (GHSA-2m69-gcr7-jv3q).
- PSBT signing proves the wallet's coins against the network before signing and again before broadcast, and never with demo data. A security self-review of the feature found no high or medium issues.

## [0.3.0] - 2026-10-27

### Added
- **Digi-ID signing address on the confirmation card** — Before you approve a Digi-ID sign-in, the card shows the full site-specific address that will identify you, with a one-tap Copy button. The address is derived locally without signing (`WalletService.GetDigiIdAddress`) through the same `DigiIdService.GetAddress` helper that `Sign()` uses, so it always matches the signed address. Hidden when the wallet is locked or watch-only.
- **Full address with copy on the "Authenticated!" card** — The success card now shows the whole address (no truncation) with a Copy button.
- **Login history details** — Each Login History row opens a details view with the domain, full callback URL, signing address, challenge (nonce), signed URI, signature, exact date and time, and the connection type (HTTP is flagged), each with its own Copy button. Closes with the button, overlay tap or Escape, and focus returns to the row. History entries saved before this release show "Not recorded" for the new fields.
- **Baku, the dragon mascot** — In-app guide and guardian with speech-bubble messages, including warnings on first-time or insecure Digi-ID sites.
- **DigiDollar** — Explorer-backed backend that indexes minted DigiDollar, DigiDollar send enabled behind an `OracleEnabled` switch, and a shimmer loading state on the home tile.
- **55 display currencies** with flags and a searchable picker.
- **New tests** — `DigiIdServiceTests` (address parity with `Sign`) and `DigiIdAddressTests` (per-site address stability, locked and watch-only wallets, legacy history JSON).

### Changed
- Login history records moved to a shared `DigiIdLoginRecord` (`DigiByte.Wallet.Models`) with optional new fields; stored `digiid_login_history` data remains compatible.
- App version bumped to 0.3.0.

## [0.9.0-beta.1] - 2026-04-16

### Added
- **Live confirmation countdown** — Every pending transaction on the Home page shows a real-time `ConfirmationBadge` component (compact: pulsing amber `3 / 6 conf` + 6-dot mini-bar; confirmed: green tick). A 15-second `System.Threading.Timer` polls `TransactionTracker.UpdateConfirmationsAsync` for all transactions with fewer than 6 confirmations; timer stops automatically when all are confirmed or the page is disposed.
- **Expanded confirmation view** — The transaction detail modal shows a full 6-step progress bar with a DGB speed callout ("~15s blocks — typically confirmed in ~90s").
- **Persistent Digi-ID login history** — Each successful Digi-ID authentication is saved to IndexedDB (`digiid_login_history`, capped at 50 entries). The history panel on the ID page shows domain, truncated address, and relative timestamp ("5m ago", "3h ago", "2d ago"). A "Clear all" button wipes the stored history.
- **31 new unit tests** — `ConfirmationPollerTests` (8 tests: pending update, fully-confirmed skip, tx-not-found, multiple partial updates, persist-to-disk, empty history, mixed confirmed/pending, same-count no-write) + `DigiIdServiceTests` (23 tests: 9 ParseUri, 6 DeriveSiteIndex, 8 Sign)

### Changed
- **Help Center updated** — Two new Q&As in the "Sending & Receiving" section (confirmation tracking) and two new Q&As in the "Digi-ID" section (login history & clear history)
- **Roadmap updated** — New "DGB-Specific UX" milestone (Apr 2026, Done): confirmation countdown + Digi-ID history + 31 tests
- **Process flows updated** — Home page flow extended with confirmation poller steps; Digi-ID flow extended with login history persistence and clear steps

## [0.7.0-beta.1] - 2026-04-12

### Added
- **Per-wallet spending limits** — Configurable daily (24h), weekly (7d), and monthly (30d) rolling DGB limits; settings stored per-wallet in IndexedDB (`spending_limit_` prefix)
- **Spending limit enforcement on send** — `PreviewSend()` checks all enabled limits; hard block prevents confirm modal, soft warn shows amber banner in review
- **Spending limit settings page** (`/settings/spending-limits`) — PIN-gated controls: enable/disable toggle, DGB limit inputs with period badges, alert threshold selector (60/70/80/90%), hard block toggle, live spending progress bars
- **Settings nav card** — "Spending Limits" card (red shield icon) in Settings page; hidden for watch-only (xpub) wallets
- **Alert threshold warnings** — Amber warning banner in send confirm modal when approaching configurable percentage of any limit
- **Spending limit cleanup** — Limit settings deleted on wallet delete alongside biometric data
- **17 new unit tests** — SpendingLimitServiceTests covering settings persistence, roundtrip, corrupt JSON, period calculations, threshold warnings, hard block, multi-period worst-result, zero-limit skip, batch amounts, per-wallet isolation

### Changed
- **Help Center updated** — New "Spending Limits" Q&A section with 4 entries covering setup, enforcement, and per-wallet behavior
- **Roadmap updated** — "Spending limits / transaction alerts" marked as completed
- **Process flows updated** — Send page flow updated with spending limit check step; new SpendingLimits page flow added

## [0.6.0-beta.1] - 2026-04-14

### Added
- **Recovery phrase masked input** — Seed phrase is hidden by default (CSS `-webkit-text-security: disc` for textarea, `type="password"` for word inputs); eye toggle to show/hide
- **Word-by-word input mode** — 3-column numbered grid for entering recovery phrase one word at a time; mode toggle transfers data between Paste All and Word by Word
- **BIP39 auto-suggest** — Dropdown shows up to 6 prefix-matched BIP39 words as you type; keyboard navigation (Arrow Up/Down, Enter/Tab to accept, Escape to dismiss); privacy mode shows first 2 chars + dots when hidden
- **Multi-separator paste** — Word inputs auto-split on spaces, commas, newlines, and tabs; pasting a full phrase into a single field distributes words across all fields
- **12/24 word count toggle** — Switch between 12 and 24 word grids; auto-expands to 24 if more than 12 words are pasted
- **Word validation** — Non-BIP39 words get red borders via binary search against the 2048-word list
- **`MnemonicGenerator.GetWordList()`** — New public API exposing the BIP39 English wordlist from NBitcoin
- **Donation page USD conversion** — Live DGB/USD price from CoinGecko via `IBlockchainService.GetDgbPriceAsync()` on Deployment and Donation History pages
- **Copy donation address** — One-tap clipboard copy with "📋 Copy Address" → "✅ Copied!" feedback animation
- **Donation data caching** — 5-minute TTL via `MemoryCacheService` for donation balance, transactions, and price (`donation:balance`, `donation:txs`, `donation:price` keys)
- **Donation refresh buttons** — Spinning arrow buttons that clear donation cache prefix and force re-fetch
- **Donation history USD values** — Each transaction on `/donations` shows its USD equivalent at the current price
- **3 new unit tests** — MnemonicGenerator wordlist tests (count, valid words, sorted order)

### Changed
- **Donation pages use BlockchainService** — Refactored from direct `IHttpClientFactory` HTTP calls to `FallbackBlockchainService` with proper fallback chain
- **Mainnet-forced donation queries** — Donation pages temporarily switch to mainnet via `SetNetworkMode()` regardless of current network setting, restoring original mode in `finally` block
- **Roadmap updated** — New "Donation Pages & Recovery UX" and "Biometric & Batch Send" milestones marked Done; security enhancements and advanced features updated
- **Help Center updated** — New Q&A for wallet recovery input modes and donation page features (14 sections total)
- **Process flows updated** — RecoverWallet section rewritten to document dual input modes, auto-suggest, and masking; Deployment section updated with donation/history page descriptions

### Fixed
- **ScriptPubKey matching on donation history** — Transactions matched by `output.ScriptHex` against the donation address's scriptPubKey for accurate filtering

## [0.5.0-beta.1] - 2026-04-11

### Added
- **Biometric unlock (WebAuthn PRF)** — Unlock your wallet with fingerprint or face recognition using the WebAuthn PRF extension; per-wallet enrollment with AES-256-GCM wrapping key derived from platform authenticator
- **Biometric enrollment prompt** — After first successful PIN unlock, a modal offers to enable biometric; "Not Now" dismisses per-wallet; can be enabled later from Settings
- **Confirm sends with biometric** — Optional setting to require biometric verification (WebAuthn assertion) before broadcasting any transaction; toggle in Settings under Biometric Unlock
- **Biometric auto-trigger on unlock** — When biometric is enrolled, the unlock screen automatically triggers biometric authentication on page load
- **Settings biometric toggle** — Enable/disable biometric unlock from Settings with PIN verification for enrollment and confirmation for disable
- **Biometric cleanup on wallet delete** — Disabling or deleting a wallet removes all biometric keys (credential, wrapping key, encrypted seed, preferences)
- **19 new unit tests** — BiometricServiceTests covering enabled/dismissed/confirm-send preferences, disable cleanup, per-wallet isolation, JS interop error handling, and identity verification

### Changed
- **Version bumped to 0.5.0-beta.1** — Biometric unlock and send confirmation feature complete
- **Production log suppression** — HTTP/Polly info logs suppressed only in production; Development keeps info-level logging
- **Font preload removed** — Removed unused `<link rel="preload">` for Inter font; `@font-face` with `font-display: swap` handles on-demand loading
- **Roadmap updated** — Biometric Unlock and Batch Send UI marked as completed
- **Help Center updated** — New Biometric & Security section with real-life scenarios, security model explanation, and confirm-send documentation

### Fixed
- **Biometric enrollment prompt race condition** — Prompt no longer shows when biometric is already enabled; `UnlockAsync` now re-checks `IsEnabledAsync` directly instead of relying on cached field from `OnAfterRenderAsync`
- **JS interop timing in Blazor WASM** — Biometric detection moved to `OnAfterRenderAsync(firstRender)` since JS interop is unavailable during `OnInitializedAsync`
- **Global scope collision** — `webauthn.js` wrapped in IIFE to prevent `const IV_SIZE` conflict with `crypto.js`
- **Font preload browser warning** — Removed preload tag that triggered "preloaded but not used" console warning

### Security
- **No secrets at rest** — Biometric wrapping key is derived from the platform authenticator via WebAuthn PRF; only encrypted blobs stored in IndexedDB
- **Per-wallet credentials** — Each wallet gets its own WebAuthn credential; disabling biometric for one wallet does not affect others
- **Identity-only send confirmation** — Send confirmation uses a simple WebAuthn assertion (no PRF/decryption), verifying only that the authenticated user is present

## [0.4.0-beta.1] - 2026-04-11

### Added
- **Multi-wallet support** — Create and manage up to 10 wallets (HD, WIF, Watch-Only) per device with independent PIN unlock per session
- **Wallet switcher** — Swipe left/right on the Home balance card to switch wallets instantly; wallet picker bottom sheet with swipe-to-dismiss
- **Stacked card carousel** — Layered card visual on Home showing wallet deck with colored ghost cards behind the active wallet
- **Wallet color theming** — 10-color palette with per-wallet color; color picker during creation and in wallet management
- **Wallet management page** — Rename, change color, delete, and backup wallets from Settings → Manage Wallets
- **Wallet picker sheet** — Bottom sheet overlay listing all wallets with color dots, type badges, "Create New Wallet" button
- **Per-wallet scoped cache** — Balance, transactions, and fetch timestamps are now scoped per wallet ID; price remains global
- **Enhanced unlock screen** — Wallet name shown in frosted-glass pill badge with color dot and glow effect
- **Recovery page wallet info** — Active wallet info card showing name, type, and color on the Recovery Tools page
- **`ChangeWalletColorAsync`** — New service method to update wallet color with persistence and in-memory session sync
- **19 new unit tests** — MultiWalletTests covering creation, switching, rename, color change, deletion, palette, and mixed wallet types

### Changed
- **Version bumped to 0.4.0-beta.1** — Multi-wallet feature complete
- **Cache freshness increased** — Stale threshold raised from 2 to 5 minutes; in-memory TTL from 5 to 10 minutes
- **Touch handlers enhanced** — Horizontal swipe detection (wallet switch) coexists with vertical pull-to-refresh
- **Roadmap updated** — Multi-Wallet Support milestone marked Done; removed from Q2 planned
- **Help Center updated** — 4 new Q&As for multi-wallet, wallet switching, color, and rename
- **Welcome page** — Footer links/version hidden in add-wallet mode for cleaner UI

### Fixed
- **Zero-balance wallet always re-fetched** — Cache required balance > 0 to count as cached; now any persisted balance is valid
- **Global in-memory timestamp** — `lastFetchTime` was shared across all wallets causing false fresh/stale decisions; now wallet-scoped
- **Removed BIP44→BIP84 migration banner** — Legacy fund sweep banner removed from Home; recovery tools handle this instead

## [0.3.0-beta.1] - 2026-04-07

### Added
- **Real-time multisig rooms** — SignalR-based collaborative multisig wallet creation with invite codes, participant tracking, ready system, auto-expiry (15 min), and deep link support
- **Multisig backup/restore** — Download redeem script as `.txt` backup file with wallet metadata, co-signers, and recovery instructions
- **Import from backup file** — Upload `.txt` backup on MultisigImport page; auto-parses wallet name and redeem script
- **Backup warning** — Amber warning banner on MultisigDetail page explaining seed phrases cannot recover multisig wallets
- **VersionBadge component** — Shared tappable version pill badge used across 5 pages (Welcome, About, CreateWallet, Settings, Unlock)
- **Version Info modal** — Tappable modal with app details, channel, platform, framework, and What's New section; rendered at layout level via static event pattern
- **Real-life scenario** — Maya/Leo/Priya vacation fund walkthrough in Multisig Guide real-time collaboration section
- **Security documentation** — Comprehensive `docs/SECURITY.md` with threat model, OWASP Top 10 mapping, risk matrix, cryptographic primitives, and hardening recommendations
- **Video tutorial** — Real-time multisig creation recording (`create-multi-sigg.mp4` + GIF) added to README

### Changed
- **Version bumped to 0.3.0-beta.1** — Promoted from alpha to beta; all core features functional, documented, and tested
- **Roadmap updated** — Added "Real-Time Multisig Rooms" and "UI Enhancements" as completed milestones
- **Help Center updated** — New Q&As for multisig rooms, real-time creation, and recovery warnings
- **Multisig Guide updated** — New "Real-Time Collaboration" expandable section with step-by-step walkthrough
- **Delete wallet help** — Updated to mention backing up multisig redeem scripts before deletion

### Fixed
- **Version Info modal positioning** — Fixed CSS `transform` containment issue (animations creating containing blocks that break `position: fixed`); modal now rendered at layout level outside page transforms

## [0.2.0-alpha.1] - 2026-04-06

### Added
- **Video tutorials** — Recorded walkthroughs in `docs/media/`: How to Install PWA, How to Recover/Import Wallet, How to Send DigiByte
- **iOS PWA install banner** — Detects iOS Safari and shows manual "Add to Home Screen" instructions (since `beforeinstallprompt` is Chromium-only)
- **Route-aware install banner** — Banner always shows on Welcome, Recover, and Watch-Only pages regardless of dismiss state
- **CoinGecko price proxy** \u2014 Server-side proxy endpoints (`/api/price/simple`, `/api/price/coin`) with 60s IMemoryCache TTL; avoids browser CORS and 429 rate limits
- **Railway deployment** \u2014 Dockerfiles for API (multi-stage .NET 10) and Web (Blazor WASM + Nginx), ForwardedHeaders, config-driven CORS
- **PWA install banner** \u2014 Native `beforeinstallprompt` capture with styled top banner (session-scoped dismiss)
- **Donation balance** \u2014 Live DGB balance shown on Deployment & Support page
- **Config-driven network defaults** \u2014 `DefaultNetwork` in appsettings (mainnet prod, testnet dev); localStorage overrides
- **Configurable fee rates** \u2014 Send page fee selection (Low 100/Normal 200/Fast 400 sat/vB) now controls actual transaction fees
- **Global error boundary** — Styled crash recovery screen with "Go Home" button; auto-recovers on navigation
- **PIN lockout protection** — Exponential backoff after 3 failed attempts (15s → 30s → 60s → 120s)
- **Multi-currency fiat** — USD ($), EUR (€), GBP (£), PHP (₱), JPY (¥) with proper currency symbols
- **Currency change cache invalidation** — Changing currency clears price caches and forces re-fetch
- **Self-hosted assets** — Tailwind CSS (purged ~37KB), Inter font (woff2), jsQR — all CSP compliant
- **DigiByte favicon/icons** — SVG favicon, 192px/512px PNG icons using official DGB logo
- **Version and tagline** — "Speed · Security · Scalability" footer on Unlock and CreateWallet pages
- **Polly resilience** — Per-client retry, circuit breaker, timeout (Blockchain 15s/45s, NodeApi 8s/20s)
- **Two-tier caching** — MemoryCacheService (TTL + dedup) + IndexedDB persistent cache
- **Production log levels** — HTTP/Polly at Warning; Development overrides to Information
- **Send page loading states** — Skeleton loaders for balance/price, form disabled during fetch
- **Amount warnings** — Real-time inline warnings for exceeding balance and below minimum
- **Developer tools guard** — Cache clear button only in Development environment
- **Recovery Options close button** — X button to dismiss Forgot PIN panel
- **Language coming soon modal** — Placeholder until multi-language is complete
- Address format labels (Legacy/SegWit/P2SH) on contacts and transaction details
- Address format selector on Receive page (Legacy/SegWit toggle)
- Transaction detail modal with explorer link, copy txid, add to contacts
- Forgot PIN recovery (verify seed phrase to reset PIN, or delete wallet)
- Delete Wallet option in Settings (PIN-protected, clears all caches)
- QR code scanner (Send, Contacts, Home scan button)
- OTP-style 6-digit PIN input on all PIN pages
- Contact book with CRUD, QR scan, search
- Payment requests with BIP21 URI generation
- Digi-ID passwordless authentication
- Toast notification system
- Transaction tracking (local IndexedDB + explorer fallback)
- Node API (87 RPC methods + Scalar docs)
- Docker setup (regtest, testnet, mainnet pruned)
- Light/dark theme with official DigiByte branding
- Settings persistence to localStorage
- Backup seed phrase (PIN-protected viewing)
- Real transaction signing and broadcasting via NBitcoin

### Fixed
- **too-long-mempool-chain broadcast error** — Blockbook fallback + confirmed-only UTXO filtering
- **bad-witness-nonstandard on send** \u2014 HD wallet UTXO path now queries each address individually; correct scriptPubKey (legacy vs segwit) per UTXO
- **Send page spinners**  \u2014 Review Transaction and Send Now buttons show loader immediately (`Task.Yield` forces render before async work)
- **MemoryCacheService TryGet** — Value types (decimal 0) no longer treated as cache hits
- **CoinGecko $0.00 price** — Services throw instead of returning 0, enabling fallback chain
- **IndexedDB price cache** — No longer loads/saves 0 values
- **Home page empty data** — Distinguishes network error from empty wallet
- **Currency not persisted** — Now saved to localStorage on change
- **Stale cache after currency change** — Clears fetch timestamp so Home re-fetches
- **Blazor input re-render** — `@bind:after` prevents cursor jumps on Send amount warnings
- Auto-detect network from WIF key prefix on import
- Load persisted preferences at app startup
- Confirmations calculated correctly (currentHeight - blockHeight + 1)

## [0.1.0] - 2026-04-03

### Added
- Initial release
- Blazor WASM PWA with DigiByte wallet core
- BIP39/BIP44 HD wallet (24-word mnemonic)
- AES-256-GCM encrypted seed storage in IndexedDB
- Send/Receive with QR codes
- DigiByte network definitions (mainnet, testnet, regtest)
