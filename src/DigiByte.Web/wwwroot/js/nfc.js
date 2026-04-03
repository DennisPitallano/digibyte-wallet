// Web NFC API wrapper for tap-to-pay
window.nfcManager = {
    supported: 'NDEFReader' in window,

    async write(data) {
        if (!this.supported) throw new Error('NFC not supported on this device');
        const writer = new NDEFReader();
        await writer.write({ records: [{ recordType: 'url', data: data }] });
    },

    async read(dotnetHelper) {
        if (!this.supported) throw new Error('NFC not supported on this device');
        const reader = new NDEFReader();
        await reader.scan();
        reader.onreading = (event) => {
            for (const record of event.message.records) {
                if (record.recordType === 'url' || record.recordType === 'text') {
                    const decoder = new TextDecoder();
                    const text = decoder.decode(record.data);
                    dotnetHelper.invokeMethodAsync('OnNfcRead', text);
                    return;
                }
            }
        };
        reader.onreadingerror = () => {
            dotnetHelper.invokeMethodAsync('OnNfcError', 'Could not read NFC tag');
        };
    },

    isSupported() {
        return this.supported;
    }
};
