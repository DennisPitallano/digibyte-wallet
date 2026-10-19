# Data Model: Baku Mascot

## BakuPose

A named mascot expression and its image.

| Field | Description |
|---|---|
| Id | Stable slug, e.g. `greeting`, `protective`, `celebrating`, `thinking` |
| ImagePath | Self-hosted transparent image under `images/mascot/poses/` |
| Description | Short text used when the image needs an accessible description |

## BakuMessage

A predefined English message tied to a situation.

| Field | Description |
|---|---|
| Id | Stable key, e.g. `backup.why`, `send.new-address`, `home.empty` |
| Severity | `Info` < `Tip` < `Warning` < `Critical` (drives priority and presentation) |
| PoseId | Pose to show |
| Title / Body | Plain-language text (English) |
| Dismissible | True for Info/Tip only. Warnings and Critical are never permanently dismissible |
| RequiresConfirmation | True for the warnings that gate an action (seed reveal, risky send) |

**Rules**: Only one message is displayed per screen at a time. Highest severity wins and ties go to the most recently requested. Critical messages always render text, even with Baku off.

## BakuPreference (device-local)

| Field | Description |
|---|---|
| Mode | `Full` (default), `GuardianOnly`, `Off` (Info and Tip hidden; Warning and Critical still shown as text) |
| DismissedTipIds | Set of Message Ids permanently dismissed |

**Transitions**: Dismissing a tip adds its Id. "Reset tips" clears the set. Changing Mode takes effect immediately and persists. Preferences contain no wallet, key or user-identifying data.

## SendRiskAssessment (computed, not stored)

| Field | Description |
|---|---|
| LargeAmount | True when amount (excluding network fee; send-max counts as 100%) >= 50% of current balance (and balance > 0) |
| NewAddress | True when destination is neither a saved contact nor previously sent to |
| Applies to | DGB sends and DigiDollar sends |

## Message Catalog (English, v1)

Severity: I = Info, T = Tip (dismissible), W = Warning, C = Critical. Confirm = requires "I understand, continue".

| Id | Screen | Sev | Pose | Title | Body |
|---|---|---|---|---|---|
| welcome.greeting | Welcome | I | greeting | Hi, I am Baku! | I will help you set up your wallet and keep it safe. |
| create.intro | CreateWallet | I | welcome | Let us create your wallet | It takes a minute. You will get a recovery phrase that only you should ever see. |
| recover.intro | RecoverWallet | I | thinking | Restoring a wallet | Enter your recovery phrase carefully, word by word, in the right order. |
| backup.why | BackupSeed | I | trustworthy | Write it down | These words are the only way to restore your wallet. Store them offline and never share them. |
| backup.done | BackupSeed | I | celebrating | Backup confirmed! | Great job. Your wallet is ready. |
| wallet.created | CreateWallet | I | celebrating | Wallet created! | You are all set to receive your first DGB. |
| unlock.greeting | Unlock | I | greeting | Good to see you | Enter your PIN to unlock your wallet. (Hidden once typing starts.) |
| seed.reveal | BackupSeed | C, Confirm | locked | Anyone can see your screen | Nobody legitimate will ever ask for your recovery phrase. Make sure no one is watching. |
| send.new-address | Send, DigiDollarSend | W, Confirm | guard | New address | You have not sent to this address before. Double-check every character with the recipient. |
| send.large-amount | Send, DigiDollarSend | W, Confirm | protective | Large send | This is half or more of your balance. Make sure the amount is right. |
| send.new-and-large | Send, DigiDollarSend | W, Confirm | protective | Check this send | New address, and half or more of your balance. Confirm both before continuing. |
| send.done | Send, DigiDollarSend | I | approve | Sent! | Your transaction is on its way. |
| send.error | Send, DigiDollarSend | I | error | That did not work | Nothing was sent. Check the details and try again. |
| p2p.verify | P2PMarketplace | W | guard | Trade carefully | Verify who you trade with. Never share your recovery phrase or pay outside the trade flow. |
| home.empty | Home | T | no-funds | No transactions yet | Use Receive to get your first DGB. |
| home.tip | Home | T | idea | Tip: back up first | Keep your recovery phrase safe before adding funds. |
| receive.tip | Receive | T | idea | Share your address | Each address can be reused, but a fresh one improves privacy. |
| dd.tip | DigiDollar | T | idea | About DigiDollar | DigiDollar is a stable-value asset on DigiByte. Send and receive it like DGB. |
| help.intro | Help | I | support | How can I help? | Search or pick a topic below. I will guide you through it. |
| help.no-results | Help | I | thinking | Nothing found | Try different words, or browse the topics below. |
| settings.tip | Settings | T | idea | Make it yours | You can change how much you see of me in Baku settings. |

Copy is a starting point and may be refined without changing ids or severities.
