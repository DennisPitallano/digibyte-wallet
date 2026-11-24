# Feature Specification: Spanish in the Language Setting

**Feature Branch**: `feature/spanish-language-option` (planned; spec directory `028-spanish-language-option`)

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "check the setting, the language options for es is still not available"

## Context

**What the person sees today.** Settings has a Language row. It always reads "English", and pressing it opens a dialog that says multi-language support is coming in a future update. There is no way to choose Spanish.

**What already exists behind it.** The wallet ships a Spanish translation of most of its shared text (about 408 of the 430 English entries; 22 are missing, among them the Settings labels for display mode, currency, network and backup, and several wallet-creation steps). The wallet already remembers a chosen language between visits and falls back to English for any text without a translation. It also lists ten languages internally, but only English and Spanish have translations. About 34 of the app's roughly 185 screens and parts read their text through translations. The rest, Settings itself included, show fixed English text.

So Spanish is "still not available" because the selector was never connected, not because the translation is missing.

## Clarifications

### Session 2026-11-24

- Q: When someone picks Spanish, which screens must be in Spanish before this ships? → A: Settings, plus the main money and safety screens: home, receive, send up to confirmation, history, wallet creation and backup, and the lock screen. Other screens stay as they are until later releases.
- Q: On a first visit, if the phone or browser is set to Spanish, should the wallet open in Spanish without being asked? → A: Yes, only on a first visit with no saved choice; a language picked in Settings always wins after that.
- Q: Who checks the Spanish text before release, especially the recovery-phrase and send warnings? → A: A native Spanish speaker must approve the security and money warnings before release; other text goes through normal review.
- Q: With Spanish chosen, should amounts use Spanish number style (1.234,56 DGB), or keep the dot as the decimal point? → A: Spanish style for amounts shown. When typing, a comma or a point is accepted as the decimal mark, and the value understood is shown before confirming.
- Q: If Spanish is chosen and someone types "1.234" as a send amount, should the wallet read it as 1.234 DGB or ask what they meant? → A: Ask. An amount with exactly one comma or point followed by exactly three digits can be read two ways, so the wallet asks which was meant before going on; any other amount follows the decimal-mark rule.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose Spanish in Settings (Priority: P1)

A Spanish-speaking person opens Settings, presses Language, sees "Español" next to "English", picks it, and the wallet switches to Spanish straight away, with no reload. The Language row now reads "Español". On the next visit the wallet opens in Spanish.

**Why this priority**: This is the reported problem. Without it, the existing Spanish translation can never be used.

**Independent Test**: In a fresh browser set to English, open Settings → Language, choose Español, and confirm the row and the translated screens (for example the DigiDollar screens) show Spanish. Reload and confirm Spanish is kept. Switch back to English and confirm it is kept too.

**Acceptance Scenarios**:

1. **Given** the wallet is in English, **When** the person opens the Language setting, **Then** they see a list with English and Español, each written in its own language, with the current one marked.
2. **Given** the list is open, **When** the person picks Español, **Then** the list closes and every screen that has translations shows Spanish without a page reload.
3. **Given** Spanish was chosen, **When** the person closes and reopens the wallet, **Then** it opens in Spanish and the Language row reads "Español".
4. **Given** Spanish is active, **When** the person picks English, **Then** the wallet returns to English and stays English on the next visit.
5. **Given** the Language list is open, **When** the person closes it without picking, **Then** nothing changes.
6. **Given** a first visit with no saved language and a browser set to Spanish, **When** the wallet opens, **Then** it shows Spanish and the Language row reads "Español".
7. **Given** the browser is set to Spanish but the person picked English in Settings, **When** the wallet opens again, **Then** it shows English.

---

### User Story 2 - Settings and core screens read in Spanish (Priority: P2)

Once Spanish is chosen, the Settings screen where the person made the choice, and the wallet's main screens (home, send, receive, history, wallet creation and backup, and the lock screen), show Spanish rather than staying in English.

**Why this priority**: Choosing Spanish and still seeing an English page is the most visible gap. The main money flows are where a misunderstanding costs the most.

**Independent Test**: With Spanish active, walk through every screen in FR-009 (Settings, welcome, create and back up a wallet, recover a wallet, lock and unlock, home, receive, send up to confirmation, history), and confirm no fixed English text remains on them, apart from names that stay the same in every language (DigiByte, DGB, DigiDollar, Digi-ID).

**Acceptance Scenarios**:

1. **Given** Spanish is active, **When** the person views Settings, **Then** the section titles, row labels, descriptions and dialogs are in Spanish.
2. **Given** Spanish is active, **When** the person creates a wallet, **Then** the recovery-phrase warnings and PIN steps are in Spanish.
3. **Given** Spanish is active, **When** a text has no Spanish translation yet, **Then** the English text appears, never a raw internal label.
4. **Given** Spanish is active and the balance is one thousand two hundred thirty-four and a half DGB, **When** the person views home, **Then** it reads 1.234,5 DGB (or with the wallet's usual number of decimals).
5. **Given** Spanish is active, **When** the person types "12,5" or "12.5" as a send amount, **Then** both are read as twelve and a half, and the confirmation shows 12,5 DGB.
6. **Given** any language, **When** the person types "1.234,5" as a send amount, **Then** the wallet does not continue and asks for the amount without thousands marks.
7. **Given** Spanish is active, **When** the person types "1.234" as a send amount, **Then** the wallet asks whether they meant 1.234 DGB (one thousand two hundred thirty-four) or 1,234 DGB (about one), and the confirmation shows the amount they pick.

---

### User Story 3 - Only offer languages that are ready (Priority: P3)

The Language list shows only languages the wallet can actually display. Languages that are listed internally but have no translation do not appear.

**Why this priority**: Offering a language that turns out to be all English is the same disappointment as the current "coming soon" dialog.

**Independent Test**: Open the Language list and confirm it holds exactly the languages that have a translation (today: English and Español).

**Acceptance Scenarios**:

1. **Given** only English and Spanish translations exist, **When** the person opens the list, **Then** exactly those two appear.
2. **Given** a saved language that is no longer offered and a browser not set to Spanish, **When** the wallet opens, **Then** it shows English and the Language row reads "English".

### Edge Cases

- The Spanish translation fails to load (offline first visit, network error): the wallet shows English, and choosing Spanish again retries.
- Device storage is unavailable (private mode, blocked site data): the switch works for this visit, and next time the wallet opens by the first-visit rule (FR-006a), without an error.
- Amounts, dates and fiat prices are shown in the chosen language's number and date style (for example 1.234,56 DGB in Spanish); an amount's value never changes (FR-013).
- A typed amount holds more than one comma or point (for example "1.234,5" or "1,234.5"): the wallet does not guess, and asks for the amount without thousands marks.
- A typed amount holds exactly one comma or point followed by exactly three digits, where the mark could be a thousands mark (for example "1.234" or "1,234"): it could mean about one or about one thousand, so the wallet asks which was meant, showing both values in the chosen style, before going on (FR-013a). Nothing is assumed if the person closes the question.
- A typed amount holds exactly one comma or point followed by any other number of digits (for example "12,5" or "0.00012345"), or followed by three digits where the mark cannot be a thousands mark (for example "0.123" or "1234.567"): the mark is the decimal mark, with no question.
- A pasted payment request or QR code carries an amount: it is read exactly as the payment standard writes it (always a point), whatever the language, and then shown in the chosen style.
- A payment link or QR code writes its amount with a comma (for example `?amount=1,5`), which the payment standard does not allow: the wallet fills in the address but leaves the amount empty for the person to type, and never reads it as 15.
- Copying an address or transaction ID: unaffected by the language.
- Long Spanish labels wrap or shorten without overlapping or pushing controls off screen at phone width.
- The wallet locks while Spanish is active: the lock screen and unlock flow show Spanish.
- The browser's own language is Spanish (any regional variant, such as Spanish for Mexico or Spain) on a first visit: the wallet opens in Spanish (FR-006a).
- The browser's language is one the wallet does not offer, or cannot be read: the wallet opens in English.
- The person picked English in Settings but the browser is Spanish: the wallet stays English on every later visit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Language row in Settings MUST open a language list instead of the "coming soon" dialog, and that dialog MUST be removed.
- **FR-002**: The list MUST show each offered language by its own name (English, Español) and mark the one in use.
- **FR-003**: The list MUST offer only languages that have a translation shipped with the wallet; today that is English and Spanish.
- **FR-004**: Choosing a language MUST apply it at once to every translated screen, without reloading the page or losing what the person was doing.
- **FR-005**: The Language row MUST show the name of the language in use, not a fixed "English".
- **FR-006**: The chosen language MUST be remembered on the device and used on the next visit; a saved value that is unknown or unreadable MUST be treated as no saved choice.
- **FR-006a**: When no language has been saved, the wallet MUST open in the browser's preferred language if it is an offered language (any regional variant of Spanish counts as Spanish), and in English otherwise. This first-visit choice is not saved as the person's choice; a language picked in Settings MUST always take precedence over the browser's language.
- **FR-007**: Any text without a translation in the chosen language MUST appear in English, never as an internal label.
- **FR-008**: The Spanish translation MUST cover every entry the English translation has, including the 22 entries missing today.
- **FR-009**: With Spanish active, the Settings screen, the language list, the welcome screen, wallet creation and backup, recovering a wallet from its phrase, the lock screen, home, receive, send (through confirmation) and history MUST show Spanish for all their text, except product and unit names that stay the same in every language.
- **FR-010**: Security and money warnings (recovery-phrase warnings, send confirmations, address warnings) MUST keep their full meaning in Spanish; none may be shortened or dropped in translation. The Spanish text of these warnings, including the PIN steps, MUST be approved by a native Spanish speaker before release, and that approval MUST be recorded with the release. Other Spanish text goes through the normal review.
- **FR-011**: Choosing a language MUST NOT send anything to any server and MUST NOT change keys, balances, addresses or any setting other than the language.
- **FR-012**: The language list MUST be usable by keyboard and screen reader and fit at phone width.
- **FR-013**: Amounts (DGB, DigiDollar and fiat) MUST be shown in the chosen language's number style on every screen. In the Send amount fields (single and batch) and the Receive request-amount field, the wallet MUST accept either a comma or a point as the decimal mark, in every language, and a single comma or point MUST be read as the decimal mark, except as FR-013a says; MUST reject an amount with more than one comma or point, with a message saying to leave out thousands marks; and the send confirmation MUST show the amount it understood, in the chosen style, before anything is sent. Amounts in payment requests and QR codes MUST be read by the payment standard's own format, never by the chosen language.
- **FR-013a**: When a typed amount has exactly one comma or point, exactly three digits after it, and one to three digits before it that do not start with 0 (so the mark could be a thousands mark), in any language, the wallet MUST NOT pick a meaning by itself. It MUST ask the person which amount they meant, showing both readings in the chosen language's style (for example "1,234 DGB" and "1.234 DGB" in Spanish), and MUST NOT go on to confirmation until one is chosen. Closing the question leaves the amount unchanged and unconfirmed. Amounts from payment requests and QR codes never trigger this question.

### Key Entities

- **Language choice**: the language the person picked; kept on the device; one of the offered languages. When unset, the browser's language decides if it is offered, otherwise English.
- **Offered language**: a language with a shipped translation; has a code and a name written in that language.
- **Translation**: the text of each wallet message in one language; English is complete and is the fallback for all others.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person can switch the wallet to Spanish from Settings in 3 taps or fewer and under 10 seconds.
- **SC-002**: 100% of the screens named in FR-009 show no English text (apart from product and unit names) when Spanish is active.
- **SC-003**: The Spanish translation has 100% of the entries the English one has.
- **SC-004**: After choosing Spanish, reopening the wallet shows Spanish on 100% of visits where device storage is available.
- **SC-005**: No "coming soon" message for language remains anywhere in the wallet.
- **SC-006**: 100% of the security and money warnings in Spanish have a recorded approval from a native Spanish speaker before release.
- **SC-007**: In testing, 0 typed amounts are read as a different value from what the person meant, across both languages and both decimal marks.

## Assumptions

- Scope is English and Spanish. The other eight languages listed internally are not offered until they have translations; adding them is separate work.
- Translating every screen of the wallet is out of scope (confirmed in Clarifications). This feature covers the screens in FR-009; other screens, such as contacts, Digi-ID, multisig and recovery, keep showing English until later releases convert them.
- The existing Spanish text is a sound base to complete. A native Spanish speaker is available to approve the security and money warnings (FR-010) before release; without that approval, the feature does not ship.
- New Spanish text follows the style the existing translation already uses: neutral Spanish for all regions, the informal "tú", and "billetera" for wallet ("Ingresa" for entering text).
- The Baku mascot messages stay English-only, as decided in spec 002, unless already translated.
- Typed amounts on other screens (DigiDollar send, multisig send) keep their current rules in this release. Spending limits get only the fix that stops "12,5" being read as 125.
- Number style follows the wallet language, not the browser. Someone whose browser is set to another language but whose wallet is in English sees English number style.
- Right-to-left layout is not needed, since neither offered language is written right to left.
