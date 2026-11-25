# Contract C3: Shop-Set Checkout Theme

This is an optional, additive interface for shops (FR-026a). Shops that don't use it see no change (SC-010).

## Query parameter on shopper pages

| Page | Parameter | Values | Effect |
|---|---|---|---|
| `/pay/{SessionId}` | `theme` | `light`, `dark` (case-insensitive) | The page renders in that theme for this view |
| `/pay/{SessionId}/receipt` | `theme` | same | same. The checkout carries the parameter into its "View receipt" link. |
| `/pay/button` | `theme` | same | same |

- **Invalid or missing values** are ignored, and the normal resolution applies: the shopper's saved choice, then the device setting.
- **Nothing is saved.** The parameter is never written to `localStorage`, so it doesn't change the shopper's preference on other sites or pages.
- **No other parameters change.** Existing `/pay/button` parameters (`to`, `amount`, `name`, `label`) and the API's `CheckoutUrl` format stay the same.

**Example redirect** (shop appends the parameter to the `CheckoutUrl` returned by the API):

```text
https://pay.example/pay/ses_abc123?theme=light
```

## Embed script (`/embed/digipay.js`)

**JavaScript API** (new optional property):

```js
DigiPay.checkout({ sessionId: 'ses_abc123', theme: 'light' });
DigiPay.button({ address: 'dgb1…', amount: 10, theme: 'dark' });
```

**Auto-bind attributes** (new optional attribute):

```html
<button data-digipay-checkout data-session-id="ses_abc123" data-theme="light">Pay</button>
<div data-digipay-button data-address="dgb1…" data-theme="dark"></div>
```

**Behaviour**:

- If `theme` is `light` or `dark`, the script appends `theme=<value>` to the iframe URL. Any other value is dropped silently.
- No other behaviour changes: modal sizing, the height `postMessage`, the close behaviour and the `clipboard-write` permission stay as they are.
- The modal's own backdrop and close button follow the requested theme where one is given. Otherwise they keep today's styling.

## Documentation

Update all three in the same PR:

- the DigiPay `/docs` page (embed section);
- `samples/*/README.md`, where they show the embed;
- the WooCommerce docs page, mentioning that the plugin can pass the parameter in a later plugin version. The plugin itself is not changed in this feature.
