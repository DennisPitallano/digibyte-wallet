# Feature Specification: DigiPay Website and Merchant Dashboard Follow the Wallet's Look and Feel

**Feature Branch**: `feature/digipay-ui-refresh` (planned; spec directory `032-digipay-ui-refresh`)

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "help me enhance the UI/UX of DigiPay website and merchant dashboard, follow our wallet UI/UX."

## Context

**Who uses DigiPay, and where.** DigiPay has three groups of people:

- **Visitors** read the public website: the home page, Features, How it works, the POS, WooCommerce and API guides, SDKs and About.
- **Shoppers** pay a merchant. They see the hosted checkout, the receipt, the tip/donation page, and the POS screen on a merchant's tablet. Shoppers never sign in and often arrive on a phone, from a shop's site or a shared payment link.
- **Merchants** sign in with Digi-ID and work in the merchant dashboard. The dashboard covers Overview, Analytics, Sessions, Payment links, Donation button, Webhook, Stores, API keys, Audit log, POS mode and Settings, plus a page for one session's detail.

**What the person sees today.** DigiPay and the wallet use the same DigiByte blue, but otherwise they look and behave like two different products:

- **Buttons and fields** are smaller than the wallet's, with small text. On phones they are harder to tap, and some phones zoom in when a field is tapped. Field borders are so faint they are hard to see.
- **Text** uses whatever font the device has. Addresses, transaction IDs and API keys do not get the wallet's clear fixed-width font, so look-alike characters are harder to tell apart.
- **Light and dark** offer only two choices, with no "match my device" option. The POS screen is always dark.
- **The dashboard** puts eleven sections behind tabs in one page. A section cannot be bookmarked or shared as a link, the browser's back button leaves the dashboard instead of going to the previous section, and on a phone every section sits behind a menu drawer.
- **Feedback** is plain: "Loading…" text, one-line empty messages, red error text that can show raw technical error messages to a shopper, and a "Copied ✓" swap instead of the wallet's notices.
- **The checkout** has no copy buttons for the address or amount. Status uses symbol glyphs. Screen readers are not told when a payment arrives.
- **Accessibility**: there is no skip link, no consistent visible focus outline, and few labels for screen readers.

**What already exists to build on.** The wallet has a written style guide (`docs/UI_STYLE.md`) with measured contrast checks. It also has shared building blocks: buttons, cards, fields, choice pills, a framed QR code, a back button, notices, loading placeholders, empty states with the Baku mascot, and a light / dark / match-device setting. DigiPay already shares the wallet's colour palette, already has light and dark styles, and already updates the checkout live as payments arrive.

## Clarifications

### Session 2026-11-25

- Q: Which DigiPay pages offer Spanish in this release? → A: None. DigiPay stays English only. A language option is out of scope.
- Q: What theme does a DigiPay page use on a first visit, before a choice is made? → A: Match my device on every DigiPay page, including the dashboard. This differs on purpose from the wallet's dark default (FR-026).
- Q: Should Baku appear on shopper pages or only in the merchant dashboard? → A: Dashboard only. The checkout, receipt, donation page and POS show no mascot, so attention stays on the merchant and the payment (FR-021).
- Q: When the wallet's style changes later, does DigiPay pick it up automatically or stay as it is until updated? → A: Automatically. Both products use one shared style source, and every change to it is checked on both products' pages before release (FR-001).
- Q: On a phone, what does the dashboard's raised middle button do? → A: New payment. It creates a payment session for the active store and shows its QR code (FR-017).
- Q: How fast must the checkout appear for a shopper on a typical phone connection? → A: The amount and QR code are usable within 2 seconds on an average mobile connection, and the checkout is no slower than before the restyle. Fonts never block the page from showing (FR-013a, SC-011).
- Q: On a laptop or desktop, does the dashboard use the full width or the wallet's narrow column? → A: A wide content area beside the left rail, capped at a comfortable maximum, for lists, tables, charts and logs. Forms and single-item pages stay wallet-width (FR-016).
- Q: When the checkout opens inside a shop's page, does it follow the shopper's device or the shop's theme? → A: A shop can optionally set light or dark for its embedded and redirected checkout. Without that setting it follows the shopper's choice or device (FR-026a).
- Q: What theme does a POS tablet start in when first paired? → A: Dark, as today. The merchant can switch it behind the manager PIN, and existing tablets stay dark (FR-013).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A shopper pays on a checkout that looks and works like the wallet (Priority: P1)

A shopper on a phone presses "Pay with DigiByte" in an online shop and lands on the DigiPay checkout. They see:

- the merchant's name and what they are paying for
- the amount in DGB (and in their currency when the merchant priced it that way)
- a framed QR code, a clear **Open in wallet** button, and the address in the wallet's fixed-width font
- copy buttons for the address and the amount
- a countdown to when the price expires

They copy the address, pay from any wallet, and the page changes to a clear "Payment received" state. That state is announced to screen readers and links to the receipt. When the price expires, or too little arrives, the page says so in plain words and tells them what to do next.

**Why this priority**: The checkout is the one DigiPay screen every paying customer sees. A confusing or untrustworthy checkout costs merchants sales. It is also the screen where looking like "the DigiByte wallet" builds the most trust.

**Independent Test**: Create a session from the dashboard, open its checkout on a phone-width screen in light and dark, and copy the address and amount. Pay it, let one expire, and underpay one. Confirm each state is clear, uses the wallet's components and wording style, is announced to a screen reader, and never shows a raw technical error.

**Acceptance Scenarios**:

1. **Given** an open checkout, **When** the shopper presses the address's copy button, **Then** the address is copied and a short notice confirms it, the same way the wallet confirms a copy.
2. **Given** an open checkout with an amount, **When** the shopper presses the amount's copy button, **Then** the exact DGB amount is copied (no currency symbol or thousands separators).
3. **Given** an open checkout, **When** it is shown, **Then** the QR code sits in the wallet's QR frame, with a logo small enough that common wallets still scan it.
4. **Given** a payment arrives, **When** the status changes, **Then** the page shows the new state with a wallet-style status badge (not symbol glyphs) and announces it to assistive technology.
5. **Given** the session cannot be loaded (wrong link, server unreachable), **When** the page opens, **Then** the shopper sees a plain message saying the payment page could not be loaded and what to try. No technical error text is shown.
6. **Given** the price has expired, **When** the shopper looks at the page, **Then** it says the price expired, shows no QR code or address to pay, and offers a way back to the shop when the merchant gave one.
7. **Given** the tip/donation page and the receipt page, **When** they are opened, **Then** they use the same components, fonts, copy buttons and states as the checkout.

---

### User Story 2 - The dashboard is easy to move around, on desktop and phone (Priority: P1)

A merchant signs in and lands on Overview. On a laptop, a left rail lists the dashboard's sections, the same way the wallet shows its menu on wide screens. On a phone, a bottom menu shows the most-used sections (Overview, Sessions, a raised **New payment** button, Payment links, More), the same way the wallet's bottom menu works. Every other section is under More. Each section has its own address, so the merchant can bookmark "Webhook", share a link to a session, and use the browser's back button to return to the previous section. Detail pages (one session) have the wallet's back button.

**Why this priority**: Merchants use the dashboard every day. Today it cannot be bookmarked, back does not work, and on a phone everything is behind a drawer. These are the most common navigation complaints, and fixing them changes how every dashboard visit feels.

**Independent Test**: Sign in on a laptop and on a phone-width screen. Visit every section from the menu, bookmark one and reopen it, use back and forward between sections, and open a session and go back. Confirm every section is reachable within two taps on a phone.

**Acceptance Scenarios**:

1. **Given** a signed-in merchant on a wide screen, **When** the dashboard opens, **Then** the sections are listed in a left rail, the current one is marked, and the active store is shown with a way to switch it.
2. **Given** a signed-in merchant on a phone-width screen, **When** the dashboard opens, **Then** a bottom menu shows the main sections and More, and the raised main button starts a new payment session.
3. **Given** the merchant opens the Webhook section, **When** they copy the browser address and open it in a new tab, **Then** the Webhook section opens directly (after sign-in if needed).
4. **Given** the merchant moves from Sessions to API keys, **When** they press the browser's back button, **Then** they return to Sessions with its filters and scroll position kept where practical.
5. **Given** the merchant opens one session, **When** they press the page's back button, **Then** they return to the Sessions list they came from.
6. **Given** a signed-out visitor opens a dashboard section's address, **When** the page loads, **Then** they are sent to Digi-ID sign-in and returned to that section afterwards.

---

### User Story 3 - One style layer across every DigiPay page (Priority: P1)

Every DigiPay page uses the same building blocks as the wallet: the website, docs, sign-in, dashboard, checkout, receipt, donation page and POS. That means buttons (primary, secondary, quiet, danger), cards, fields, choice pills, headings, section labels, icon buttons, the back button, the QR frame, notices, loading placeholders and empty states, with the wallet's fonts and motion. A merchant moving between the wallet and DigiPay feels they are in one family of products.

**Why this priority**: This is the core of the request ("follow our wallet UI/UX"). It also fixes measurable problems: small tap targets, faint field borders, and phone zoom on small text.

**Independent Test**: Open every DigiPay page in light and dark at phone and desktop widths next to the wallet's style gallery. Confirm that buttons, fields, cards and headings match in size, shape, colour and font. Run the same contrast and tap-size checks the wallet's style guide records.

**Acceptance Scenarios**:

1. **Given** any DigiPay page, **When** it shows a button, **Then** the button matches the wallet's button for its role in height, shape, colour and text size.
2. **Given** any DigiPay form field, **When** it is tapped on a phone, **Then** the page does not zoom in, and the field's border meets the same contrast level as the wallet's fields.
3. **Given** an address, transaction ID, API key or code snippet, **When** it is shown, **Then** it uses the wallet's fixed-width font.
4. **Given** a destructive action (revoke an API key, void a session, unpair a POS device, refund), **When** its button is shown, **Then** it uses the wallet's danger style and asks for confirmation the way the wallet does.

---

### User Story 4 - Clear loading, empty, error and success feedback (Priority: P2)

Lists and panels show loading placeholders shaped like the content, not "Loading…" text. Empty sections explain what goes there and offer the next step, with Baku where the wallet would use him (in the dashboard only, never on shopper pages). For example, empty Sessions offers "Create your first payment", and a store with no receive address points to Settings. Actions such as saving settings, creating a key, copying a link or sending a test webhook confirm with the wallet's notices. Failures explain what went wrong in plain words and how to retry.

**Why this priority**: These states make the dashboard feel finished and guide new merchants through setup. They depend on the shared style layer (Story 3), so they come after it.

**Independent Test**: Use a brand-new merchant account, then a slow connection, then an unreachable server. Visit every section and confirm each shows a shaped placeholder while loading, a helpful empty state with a next step, a notice on success, and a plain retry message on failure.

**Acceptance Scenarios**:

1. **Given** a section is loading, **When** it is shown, **Then** placeholders shaped like its content appear instead of "Loading…" text.
2. **Given** a new merchant with no sessions, **When** they open Sessions, **Then** an empty state explains what sessions are and offers to create one.
3. **Given** a store with no receive address set, **When** the merchant opens Overview or tries to create a session, **Then** they are told why payments cannot be created yet and taken to the setting that fixes it.
4. **Given** the merchant saves settings or creates an API key, **When** it succeeds, **Then** a wallet-style notice confirms it. When it fails, a notice or inline message says what happened and offers a retry.
5. **Given** webhook deliveries are failing, **When** the merchant opens Overview or Webhook, **Then** a clearly toned banner says deliveries are failing and links to the delivery log.

---

### User Story 5 - Light, dark or match my device, everywhere (Priority: P2)

Visitors, shoppers and merchants can choose Light, Dark or Match my device, the same three choices as the wallet, from the site menu or dashboard settings. The choice is remembered on that device. Every page honours it, including the checkout. On the POS screen, the merchant chooses the theme in POS settings, because shoppers cannot change it on a shared tablet.

**Why this priority**: DigiPay already has light and dark. This aligns the choices and labels with the wallet and fills the gaps (no "match my device", and the POS is always dark).

**Independent Test**: On each page type, switch between the three choices and change the device's own setting while "Match my device" is chosen. Reload, and confirm the page follows the choice with no flash of the wrong theme.

**Acceptance Scenarios**:

1. **Given** a first visit, **When** any DigiPay page opens, **Then** it follows the device's light/dark setting.
2. **Given** the person picks Dark, **When** they open any other DigiPay page on that device, **Then** it is dark from the first frame.
3. **Given** the theme picker, **When** it is shown, **Then** it offers Light, Dark and Match my device as labelled choices (not bare symbols), styled like the wallet's segmented control.
4. **Given** a paired POS tablet, **When** the merchant changes the POS theme in POS settings, **Then** the POS screen switches and keeps that choice after a restart.

---

### User Story 6 - Accessible to keyboard and screen-reader users (Priority: P2)

A merchant using only a keyboard can reach every menu item, button and field in a sensible order, always sees where the focus is, and can skip straight to the main content. A screen-reader user hears page titles, section names, button purposes, dialog titles and status changes, matching the wallet's accessibility checks.

**Why this priority**: Merchants and shoppers include people who use assistive technology. The wallet already meets these checks, so DigiPay should too. Most of this comes from using the shared building blocks, which is why it follows Story 3.

**Independent Test**: Go through sign-in, the dashboard sections, the checkout and the POS screen using only the keyboard and then with a screen reader. Run an automated accessibility checker on each page type and confirm there are no serious or critical findings.

**Acceptance Scenarios**:

1. **Given** any DigiPay page, **When** the person presses Tab first, **Then** a "Skip to main content" link appears and works.
2. **Given** keyboard navigation, **When** focus moves, **Then** a clearly visible focus outline shows on every interactive element in light and dark.
3. **Given** a dialog opens (POS manager PIN, unpair, confirm revoke), **When** it is shown, **Then** it is announced as a dialog with its title, focus moves into it and stays inside it, and Escape or Cancel closes it and returns focus.
4. **Given** a toggle or choice (store switcher, pairing method, theme), **When** it changes, **Then** its selected state is exposed to assistive technology.

---

### User Story 7 - The public website feels like the wallet's home (Priority: P3)

A visitor arriving from the wallet sees the same fonts, buttons, cards and motion on the DigiPay home, Features, guides, SDKs and About pages. On a phone, the site menu opens as a wallet-style sheet rather than a plain dropdown. Code samples and API reference blocks use the wallet's fixed-width font, with copy buttons that confirm with a notice. The page content and structure stay the same. This is a restyle, not a rewrite.

**Why this priority**: The website matters for adoption, but merchants and shoppers feel the dashboard and checkout far more. It is also the easiest part to do last, because it mostly reuses the shared style layer.

**Independent Test**: Open each public page at phone and desktop widths in light and dark. Confirm the components match the wallet's gallery, the phone menu opens and closes by touch and keyboard, and every code block's copy button works.

**Acceptance Scenarios**:

1. **Given** any public page, **When** it loads, **Then** headings, buttons, cards and links use the shared building blocks and fonts.
2. **Given** a phone-width screen, **When** the visitor opens the site menu, **Then** it opens as a sheet that can be closed by a close button, by tapping outside it, or by pressing Escape.
3. **Given** a code sample, **When** its copy button is pressed, **Then** the code is copied and a notice confirms it.
4. **Given** the visitor has asked their device to reduce motion, **When** a page loads, **Then** entrance animations are skipped, as in the wallet.

---

### Edge Cases

- **Checkout inside a shop's embedded window.** The checkout can open inside a shop's page as a small embedded window. It must stay usable at a width of 320px, with copy buttons and the QR code fully visible and nothing cut off. If the shop set a theme, the window uses it, so a dark-mode phone on a light shop still sees a light checkout.
- **Checkout status changes while a notice is showing.** The status announcement wins. A copy notice must not hide the "Payment received" state.
- **Live updates drop.** If live updates stop, the checkout says it is reconnecting, keeps showing the last known state, and keeps checking for payment. It never shows "expired" or "failed" only because the connection dropped.
- **Copying is blocked.** If the browser blocks copying, the value is selected so the person can copy it by hand, and a notice explains this.
- **POS on a shared tablet.** Shoppers cannot change the POS theme. Only the merchant can, behind the existing manager PIN. The POS keeps the large keypad and full-screen layout. Only its styling follows the wallet.
- **A merchant has many stores.** The store switcher stays usable with 20 or more stores (it can be searched or scrolled) in both the rail and the phone menu.
- **A section address the merchant cannot use.** Opening a section for a store the merchant does not own, or a section address that does not exist, shows a plain "not found" page in DigiPay style, with a way back to Overview. It never shows another merchant's data.
- **Long values.** Long store names, labels, addresses and transaction IDs wrap or are shortened with the full value available to copy. They never push the layout sideways on a phone.
- **Printing a receipt.** The receipt still prints cleanly on white paper in either theme.
- **Old bookmarks.** Existing links to `/merchant`, `/pay/{id}`, `/pay/{id}/receipt`, `/pay/button` and the docs pages keep working.

## Requirements *(mandatory)*

### Functional Requirements

**Shared style layer**

- **FR-001**: Every DigiPay page MUST use the same visual building blocks as the wallet, as described in the wallet's style guide. That covers buttons (primary, secondary, quiet, danger, small), cards, text fields, choice pills, segmented choices, headings, section labels, icon buttons, the back button, the QR frame, notices, loading placeholders and empty states. These building blocks MUST come from one style source shared with the wallet, so a change to that source reaches both products. Any change to the shared source MUST be checked on both the wallet's and DigiPay's pages (including the checkout) before it is released.
- **FR-002**: DigiPay MUST use the wallet's fonts: the wallet's main typeface for text, and its fixed-width typeface for addresses, transaction IDs, API keys, codes and code samples. The fonts MUST be served by DigiPay itself, not by a third-party font service.
- **FR-003**: Buttons and other tap targets MUST meet the wallet's minimum sizes (48px for standard buttons and 44px for small ones). Text fields MUST use text large enough that phones do not zoom in when they are tapped.
- **FR-004**: Text, field borders, focus outlines and status colours MUST meet at least the contrast levels the wallet's style guide records, in light and dark.
- **FR-005**: Motion MUST follow the wallet's motion rules and MUST be skipped when the device asks for reduced motion.
- **FR-006**: Icons MUST come from one consistent set that matches the wallet's style. Status MUST NOT be shown with emoji or symbol glyphs alone.

**Checkout, receipt, donation page and POS (shopper-facing)**

- **FR-007**: The checkout MUST show the merchant's name, the label, the amount (DGB, and the shopper's currency when priced that way), a framed QR code, an **Open in wallet** action, the address, and the expiry countdown.
- **FR-008**: The checkout, donation page and receipt MUST offer copy buttons for every value a shopper may need to paste (address, DGB amount, transaction ID). Each copy MUST confirm with a wallet-style notice. When copying is blocked, the value MUST be selected for manual copy.
- **FR-009**: Every checkout state (waiting, payment detected, payment received, confirmed, underpaid, expired, refunded, voided, plus the reconnecting and could-not-load states of FR-010 and FR-011) MUST be shown with a wallet-style status badge, a plain-language explanation, and the next step. A status change MUST be announced to assistive technology. A price change while waiting is shown as a warning banner, not a separate state.
- **FR-010**: Shopper-facing pages MUST NOT show technical error text. Load and connection failures MUST show a plain message and a way to retry or return to the shop.
- **FR-011**: If live updates are interrupted, the checkout MUST say it is reconnecting and keep showing the last known state, and MUST NOT show a final state (expired, failed) only because the connection dropped.
- **FR-012**: The checkout MUST stay fully usable at 320px wide, including inside a shop's embedded window.
- **FR-013**: The POS screen MUST keep its full-screen keypad layout and pairing flows, restyled with the shared building blocks. Its theme MUST be chosen in POS settings behind the existing manager PIN. It MUST start dark on a newly paired tablet, and tablets already paired MUST stay dark until the merchant changes it.
- **FR-013a**: On a throttled mobile connection (Chrome DevTools "Fast 4G" profile: about 9 Mbps down, 150 ms latency), on a 375px-wide phone viewport, the checkout MUST show a usable amount and QR code within 2 seconds, and MUST NOT load slower than it did before the restyle. Text MUST show straight away in the device's own font while the wallet's fonts load.

**Merchant dashboard navigation**

- **FR-014**: Each dashboard section (Overview, Analytics, Sessions, Payment links, Donation button, Webhook, Stores, API keys, Audit log, POS mode, Settings) MUST have its own address. Opening that address MUST open the section, after sign-in if needed.
- **FR-015**: The browser's back and forward buttons MUST move between dashboard sections and detail pages the merchant visited.
- **FR-016**: On wide screens the dashboard MUST list its sections in a left rail that matches the wallet's wide-screen menu, with the current section marked and the active store shown with a switcher. Beside the rail, lists, tables, charts and logs MUST use a wide content area capped at a comfortable maximum width. Forms and single-item pages (Settings, a new payment, one session) MUST stay in a narrow, wallet-width column.
- **FR-017**: On phone-width screens the dashboard MUST show a bottom menu that matches the wallet's: Overview, Sessions, a raised **New payment** main button, Payment links, and More, with every other section under More. **New payment** opens the form for a new payment session for the active store and then shows that session's QR code. If the store cannot accept payments yet, it explains why instead (FR-023). Every section MUST be reachable within two taps.
- **FR-018**: Detail pages (such as one session) MUST show the wallet's back button, which returns to the list the merchant came from.
- **FR-019**: Existing addresses for the dashboard, checkout, receipt, donation page and docs MUST keep working.

**Feedback states**

- **FR-020**: Every dashboard list and panel MUST show content-shaped loading placeholders while loading, instead of "Loading…" text.
- **FR-021**: Every dashboard section MUST have an empty state that explains what belongs there and offers the next step. Baku MUST appear in the dashboard's empty and guidance states where the wallet would use him. Shopper-facing pages (checkout, receipt, donation page, POS) MUST NOT show Baku.
- **FR-022**: Successful actions (save, create, revoke, copy, test webhook, pair device) MUST confirm with a wallet-style notice. Failures MUST say what went wrong in plain words and offer a retry where one makes sense.
- **FR-023**: When a store cannot accept payments (no receive address or key set), Overview and New payment MUST say why and link to the setting that fixes it.
- **FR-024**: When webhook deliveries are failing, Overview and Webhook MUST show a banner that links to the delivery log.
- **FR-025**: Destructive actions (revoke API key, void, refund, unpair device, remove store) MUST use the danger style and ask for confirmation in a wallet-style dialog.

**Theme**

- **FR-026**: Every DigiPay page except the POS screen MUST offer Light, Dark and Match my device as labelled choices, remember the choice on that device, and default to Match my device (the POS screen's theme is set by the merchant in POS settings and starts dark; see FR-013).
- **FR-026a**: A shop MAY set light or dark for the checkout it opens, both embedded and redirected. When a shop sets one, the checkout MUST use it. Otherwise the checkout uses the shopper's saved choice, or follows their device. Shops that set nothing MUST see no change in behaviour.
- **FR-027**: Pages MUST open in the chosen theme with no visible flash of the other theme.
- **FR-028**: The receipt MUST print legibly on white paper in either theme.

**Accessibility**

- **FR-029**: Every page MUST have a working "Skip to main content" link and a visible focus outline on every interactive element.
- **FR-030**: Dialogs MUST be announced with their title, keep focus inside while open, close with Escape or Cancel, and return focus to where it was.
- **FR-031**: Toggles and choices MUST expose their selected state, and icon-only buttons MUST have an accessible name.
- **FR-032**: An automated accessibility check of each page type MUST report no serious or critical findings.

**Public website**

- **FR-033**: The public pages MUST be restyled with the shared building blocks while keeping their current content and structure.
- **FR-034**: On phone-width screens, the site menu MUST open as a wallet-style sheet that closes with a close button, a tap outside, or Escape.
- **FR-035**: Code samples and API reference blocks MUST use the fixed-width font and have copy buttons that confirm with a notice.

**Boundaries**

- **FR-036**: This feature MUST NOT change what DigiPay does with payments, keys, webhooks or merchant data. Sign-in stays Digi-ID only. Merchant APIs, webhook payloads and the embed script's behaviour for shops MUST stay compatible. The only addition is the optional checkout theme setting (FR-026a), which shops do not have to use.
- **FR-037**: The DigiPay style guide MUST be documented alongside the wallet's, saying which building blocks are shared and where DigiPay differs (for example the POS keypad).

### Key Entities

- **Shared building block**: A visual component with a defined role, sizes, states and contrast (button, card, field, notice, placeholder, empty state, QR frame, menu). It is defined once and used by both the wallet and DigiPay.
- **Theme preference**: Light, Dark or Match my device, remembered per device for the website, dashboard and checkout, and separately per paired POS tablet.
- **Dashboard section**: A named area of the dashboard with its own address, menu entry, empty state and loading state.
- **Checkout state**: One of the payment states a shopper can see, each with its badge, explanation, next step and announcement text.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of DigiPay page types (website, docs, sign-in, every dashboard section, session detail, checkout, receipt, donation page, POS) use the shared building blocks, checked page by page against the wallet's style gallery in light and dark.
- **SC-002**: 100% of buttons and tap targets meet the wallet's minimum sizes, and 100% of text, field borders and focus outlines meet the contrast levels in the wallet's style guide. This is measured with the same checks the style guide records.
- **SC-003**: An automated accessibility check reports zero serious or critical findings on every page type (FR-032).
- **SC-004**: In a hands-on test with at least five people unfamiliar with DigiPay, every participant completes a checkout payment on a phone (copy address, pay, see "received") without help, and at least four of five say it looked trustworthy.
- **SC-005**: In the same kind of test with at least five merchants, every participant reaches any named dashboard section in under 10 seconds on desktop and on a phone, and returns to the previous section with the back button.
- **SC-006**: Every dashboard section can be bookmarked and reopened directly. Every section is reachable within two taps on a phone.
- **SC-007**: No shopper-facing page shows technical error text in any failure tested: bad link, server unreachable, live updates dropped.
- **SC-008**: No text overflows its button, badge or menu item, and nothing pushes the layout sideways, at 320px wide.
- **SC-009**: Every page opens in the chosen theme with no visible flash, verified on a cold load in each of the three theme choices (FR-027).
- **SC-010**: All existing DigiPay addresses (dashboard, checkout, receipt, donation page, docs) still open the right page, and existing shop integrations (embed script, WooCommerce plugin, sample stores) still complete a payment unchanged.
- **SC-011**: On the same throttled mobile connection as FR-013a, measured on a cold load, the checkout's amount and QR code are usable within 2 seconds in at least 95% of test runs, and the median load time is no worse than before the restyle (FR-013a).

## Assumptions

- **Restyle, not redesign.** "Follow our wallet UI/UX" means DigiPay adopts the wallet's existing style guide, components, fonts, motion, theme choices, notices and empty states. Neither product gets a new visual identity.
- **Sharing the style source keeps DigiPay independent.** DigiPay takes a copy of the shared styles when it is built. It never loads them from the wallet's site while running, so DigiPay still deploys and runs on its own, as the constitution requires (Principle IV).
- **The wallet's style guide is the source of truth.** Where DigiPay and the wallet disagree, the wallet wins. Where DigiPay needs something the wallet does not have (data tables, code blocks, the POS keypad, analytics charts), it is designed in the same style and added to the DigiPay part of the style guide.
- **Content stays.** Website and docs content stay as they are. Only presentation and navigation change.
- **The theme choice is not shared between the wallet and DigiPay.** They run on separate sites, so the theme choice is remembered separately on each, with the same choices and defaults.
- **Default theme is Match my device** for DigiPay. The wallet defaults to dark, but DigiPay's shoppers arrive from many different shops, and following the device suits them better.
- **The POS stays a kiosk.** Its theme is controlled by the merchant behind the existing manager PIN. Its layout (big keypad, full-screen QR) stays.
- **Dashboard tables stay tables on wide screens** and become stacked cards on phones, in the wallet's card style.
- **English only.** DigiPay stays in English in this release. A language option (like the wallet's Spanish) is out of scope and can follow as its own feature. Amounts and dates keep their current English formatting.
- **No behaviour changes.** Payment logic, Digi-ID sign-in, merchant APIs, webhooks, data and the shop integrations stay as they are (FR-036). This keeps the feature outside the constitution's custody, crypto and auth rules. Self-hosted fonts and assets keep within the strict content-security rules.
- **Graceful degradation.** The restyle adds no new backend dependency. The existing live-update fallback and the cached dashboard behaviour stay, with clearer reconnecting messages (FR-011).
- **The work can ship in stages**, in priority order: checkout and the shared style layer first (P1), then dashboard navigation (P1), then feedback states, theme and accessibility (P2), then the public website (P3).
