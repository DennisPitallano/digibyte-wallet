// Web Share API helper for the dashboard's payment-links tab.
// Pulled out into its own file so the Blazor circuit can call it via JS interop
// without inlining script blobs into the component. Throws when the browser
// lacks navigator.share so the C# side can fall back to clipboard copy.
window.digipayShareLink = async function (data) {
    if (!navigator.share) {
        throw new Error('Web Share API not supported');
    }
    await navigator.share(data);
};
