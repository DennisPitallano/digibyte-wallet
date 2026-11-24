# Translation review: Spanish security and money warnings

The Spanish text of every warning below must be approved by a native Spanish speaker before the release that announces Spanish (FR-010, SC-006). **The release is blocked while any row has no approver.**

How to review:

- Read the English and the Spanish side by side. The Spanish must say everything the English says: no step, risk or "never" may be softened or left out.
- Style: neutral Spanish for all regions, informal "tú", "cartera" for wallet, "frase de recuperación" for recovery phrase. DigiByte, DGB, DigiDollar and Digi-ID stay untranslated.
- If you change a text, edit it in `src/DigiByte.Web/wwwroot/locales/es.json`, update the row here, then fill in your name and the date.

| key | English | Español | approvedBy | date |
|-----|---------|---------|------------|------|
| identity.addrWarning.title | Not for receiving payments | No sirve para recibir pagos | | |
| identity.addrWarning.body | This is an identity address, used only to sign in to this site. The wallet does not watch it, so any funds sent here will not show in your balance. Never use it to receive payments. | Esta es una dirección de identidad, usada solo para iniciar sesión en este sitio. La cartera no la vigila, así que los fondos enviados aquí no aparecerán en tu saldo. Nunca la uses para recibir pagos. | | |
| identity.addrWarning.link | Sent funds here by mistake? Recover them | ¿Enviaste fondos aquí por error? Recupéralos | | |
| create.phrase_warning | Write these 24 words down and store them safely. They are the only way to recover your wallet. | Anota estas 24 palabras y guárdalas en un lugar seguro. Son la única forma de recuperar tu billetera. | | |
| amount.ambiguous.choice | {0} DGB | {0} DGB | | |
| amount.ambiguous.question | Did you mean: | ¿Quisiste decir: | | |
| backup.never_share | {0} these words with anyone. Anyone with this phrase can steal your funds. | {0} estas palabras con nadie. Cualquiera que tenga esta frase puede robar tus fondos. | | |
| backup.never_share_lead | Never share | Nunca compartas | | |
| backup.no_wallet | No wallet found | No se encontró ninguna billetera | | |
| backup.page_title | Backup Seed Phrase | Copia de seguridad de la frase de recuperación | | |
| backup.retrieve_failed | Failed to retrieve seed phrase | No se pudo obtener la frase de recuperación | | |
| backup.verify_title | Verify Your Identity | Verifica tu identidad | | |
| banner.node.fallback | The wallet team's node is unavailable. Using public explorers instead. | El nodo del equipo de la billetera no está disponible. Se usan exploradores públicos en su lugar. | | |
| banner.node.unreachable | Can't reach the blockchain right now. Showing your last known balance, which may be out of date. Sending is unavailable until the connection returns. | No se puede conectar con la blockchain ahora mismo. Se muestra tu último saldo conocido, que puede estar desactualizado. No podrás enviar hasta que vuelva la conexión. | | |
| biometric.enroll.cancelled | Biometric setup was cancelled or failed. | La configuración biométrica se canceló o falló. | | |
| biometric.enroll.failed | Biometric setup failed. You can try again from Settings. | Falló la configuración biométrica. Puedes intentarlo de nuevo desde Ajustes. | | |
| biometric.enroll.title | Enable Biometric Unlock? | ¿Activar el desbloqueo biométrico? | | |
| create.confirm_placeholder | Enter word #{0} | Ingresa la palabra n.º {0} | | |
| create.confirm_prompt | Enter word {0} from your recovery phrase | Ingresa la palabra {0} de tu frase de recuperación | | |
| create.confirm_title | Confirm Recovery Phrase | Confirma tu frase de recuperación | | |
| create.failed | Failed to create wallet: {0} | No se pudo crear la billetera: {0} | | |
| create.phrase_title | Your Recovery Phrase | Tu frase de recuperación | | |
| create.phrase_warning.lead | Write these 24 words down and store them safely. They are the {0} way to recover your wallet. | Anota estas 24 palabras y guárdalas en un lugar seguro. Son la {0} forma de recuperar tu billetera. | | |
| create.phrase_warning.only | only | única | | |
| create.pin_change | Change PIN | Cambiar PIN | | |
| create.pin_choose | Choose a 6-digit PIN to secure your wallet | Elige un PIN de 6 dígitos para proteger tu billetera | | |
| create.pin_confirm | Confirm PIN | Confirma el PIN | | |
| create.pin_enter | Enter PIN | Ingresa el PIN | | |
| create.pin_hint | Choose a PIN to unlock your wallet every time you open the app. | Elige un PIN para desbloquear tu billetera cada vez que abras la app. | | |
| create.pin_incomplete | Enter all 6 digits | Ingresa los 6 dígitos | | |
| create.pin_mismatch | PINs don't match. Try again. | Los PIN no coinciden. Inténtalo de nuevo. | | |
| create.set_pin | Set Your PIN | Define tu PIN | | |
| create.show_again | Show phrase again | Ver la frase otra vez | | |
| create.word_incorrect | Incorrect word. Please try again. | Palabra incorrecta. Inténtalo de nuevo. | | |
| create.written | I've Written It Down | Ya las anoté | | |
| history.detail.dd_note | DigiDollar amounts of this transaction's outputs. A transfer lists what was sent and the change. No DGB moved except the network fee. | Importes en DigiDollar de las salidas de esta transacción. Una transferencia muestra lo enviado y el cambio. No se movió DGB salvo la comisión de red. | | |
| history.summary.dd | {0}: {1} (network fee {2} DGB) | {0}: {1} (comisión de red {2} DGB) | | |
| history.verify.checked.hint | Checked: this wallet matched the transaction to block headers it checked itself. It is not protection against a dishonest data source. | Comprobada: esta billetera cotejó la transacción con cabeceras de bloque que comprobó por sí misma. No protege contra una fuente de datos deshonesta. | | |
| history.verify.disputed.hint | Disputed: the data sources disagree about this transaction. It is not counted as spendable until that is resolved. | En disputa: las fuentes de datos no coinciden sobre esta transacción. No se cuenta como disponible para gastar hasta que se resuelva. | | |
| history.verify.rejected.hint | Rejected: the proof for this transaction did not match the block headers this wallet checked. | Rechazada: la prueba de esta transacción no coincidió con las cabeceras de bloque que comprobó esta billetera. | | |
| history.verify.unchecked.hint | Unchecked: this transaction came from a public explorer and has not been matched to block headers yet. | Sin comprobar: esta transacción vino de un explorador público y aún no se ha cotejado con cabeceras de bloque. | | |
| home.info.network.hint | Mainnet is the real DigiByte network where DGB has actual value. Testnet is for developers to test without real money. | Mainnet es la red real de DigiByte, donde DGB tiene valor real. Testnet es para que los desarrolladores hagan pruebas sin dinero real. | | |
| home.info.path.hint | The derivation path used to generate your addresses. 84 = BIP84 standard (native SegWit), 20 = DigiByte coin type, 0 = first account. This ensures any BIP84-compatible wallet can restore your addresses from the same seed phrase. | La ruta de derivación con la que se generan tus direcciones. 84 = estándar BIP84 (SegWit nativo), 20 = tipo de moneda DigiByte, 0 = primera cuenta. Así cualquier billetera compatible con BIP84 puede restaurar tus direcciones con la misma frase de recuperación. | | |
| home.info.security.hint | Your keys never leave this device. No company or server holds your funds — only you control your wallet. If you lose your seed phrase or private key, nobody can recover it for you. | Tus claves nunca salen de este dispositivo. Ninguna empresa ni servidor guarda tus fondos: solo tú controlas tu billetera. Si pierdes tu frase de recuperación o tu clave privada, nadie puede recuperarla por ti. | | |
| home.info.type.hd.hint | Hierarchical Deterministic wallet created from a seed phrase. Can generate many addresses from one backup — like having unlimited account numbers from one master key. | Billetera jerárquica determinista creada a partir de una frase de recuperación. Puede generar muchas direcciones desde una sola copia de seguridad, como tener números de cuenta ilimitados a partir de una clave maestra. | | |
| home.info.type.wif.hint | Imported from a single private key. Has one fixed address — like having one bank account number. | Importada desde una sola clave privada. Tiene una dirección fija, como tener un único número de cuenta bancaria. | | |
| receive.error.save_failed | Could not save the new address. Your current address is still good to use. | No se pudo guardar la nueva dirección. Tu dirección actual sigue siendo válida. | | |
| receive.error.too_many_unused.paid | You have {0} unused addresses in a row. Use one of them first: the wallet only keeps looking {0} addresses past the last one that was paid. | Tienes {0} direcciones sin usar seguidas. Usa una de ellas primero: la billetera solo sigue buscando {0} direcciones después de la última que recibió un pago. | | |
| receive.error.too_many_unused.seed | You have {0} unused addresses in a row. Use one of them first: restoring from your seed only looks {0} addresses past the last one used. | Tienes {0} direcciones sin usar seguidas. Usa una de ellas primero: al restaurar desde tu frase de recuperación solo se buscan {0} direcciones después de la última usada. | | |
| receive.used_warning | This address has been used before. A new address is better for privacy. | Esta dirección ya se ha usado. Una dirección nueva es mejor para tu privacidad. | | |
| recover.address_preview | Address Preview | Vista previa de la dirección | | |
| recover.failed | Failed to recover wallet: {0} | No se pudo recuperar la billetera: {0} | | |
| recover.hide_phrase | Hide phrase | Ocultar la frase | | |
| recover.invalid_phrase | Invalid recovery phrase. Please check your words. | La frase de recuperación no es válida. Revisa tus palabras. | | |
| recover.missing.one | Missing word: {0} | Falta la palabra: {0} | | |
| recover.missing.other | Missing words: {0} | Faltan las palabras: {0} | | |
| recover.mode_key | Private Key | Clave privada | | |
| recover.mode_phrase | Recovery Phrase | Frase de recuperación | | |
| recover.not_recognised.one | · {0} not recognised | · {0} no reconocida | | |
| recover.not_recognised.other | · {0} not recognised | · {0} no reconocidas | | |
| recover.paste_hint | Paste your 12 or 24-word recovery phrase. | Pega tu frase de recuperación de 12 o 24 palabras. | | |
| recover.phrase_placeholder | Enter your recovery phrase separated by spaces... | Ingresa tu frase de recuperación separada por espacios... | | |
| recover.show_phrase | Show phrase | Mostrar la frase | | |
| recover.tip_paste | Tip: paste the whole phrase into box 1 and it fills every box. | Consejo: pega la frase completa en la casilla 1 y se llenarán todas las casillas. | | |
| recover.wif_empty | Enter a WIF private key. | Ingresa una clave privada WIF. | | |
| recover.wif_hint | Enter your WIF private key from a paper wallet or export. | Ingresa tu clave privada WIF de una billetera de papel o una exportación. | | |
| recover.wif_invalid | Invalid private key format. Expected WIF format (starts with 5, K, or L). | El formato de la clave privada no es válido. Se espera formato WIF (empieza por 5, K o L). | | |
| recover.word_by_word_hint | Enter each word of your recovery phrase. | Ingresa cada palabra de tu frase de recuperación. | | |
| recover.wrong_count | Expected 12 or 24 words, got {0}. | Se esperaban 12 o 24 palabras y hay {0}. | | |
| send.batch_info.all_desc | if it fails, no one gets paid | si falla, nadie recibe el pago | | |
| send.batch_info.privacy_desc | all addresses visible in the same tx | todas las direcciones son visibles en la misma transacción | | |
| send.confirm.check_address | Check the first and last groups against the address you were given. | Compara el primer y el último grupo con la dirección que te dieron. | | |
| send.confirm.describe | Send {0} DGB to {1} | Enviar {0} DGB a {1} | | |
| send.confirm.describe_batch | Send {0} DGB to {1} recipients | Enviar {0} DGB a {1} destinatarios | | |
| send.confirm.slide | Slide to send | Desliza para enviar | | |
| send.confirm.title | Confirm Send | Confirmar envío | | |
| send.confirm.title_batch | Confirm Batch Send | Confirmar envío múltiple | | |
| send.confirm.unconfirmed | Some funds are still unconfirmed (~15s). This is safe on DigiByte. | Algunos fondos aún no están confirmados (~15 s). En DigiByte esto es seguro. | | |
| send.error.batch_insufficient | Insufficient balance. Need ~{0} DGB but have {1} DGB. | Saldo insuficiente. Necesitas ~{0} DGB, pero tienes {1} DGB. | | |
| send.error.below_minimum | Minimum send amount is 0.0001 DGB. | El importe mínimo de envío es 0,0001 DGB. | | |
| send.error.below_minimum_batch | Below minimum (0.0001 DGB). | Por debajo del mínimo (0,0001 DGB). | | |
| send.error.biometric | Biometric verification failed or was cancelled. | La verificación biométrica falló o se canceló. | | |
| send.error.duplicate_address | Duplicate address. | Dirección duplicada. | | |
| send.error.dust | Amount is too small (dust). Please send a larger amount. | El importe es demasiado pequeño (polvo). Envía un importe mayor. | | |
| send.error.fee_too_low | Transaction fee is too low. Try increasing the fee rate. | La comisión de la transacción es demasiado baja. Prueba a subir la tasa de comisión. | | |
| send.error.inputs_spent | Some inputs have already been spent. Pull to refresh your balance and try again. | Algunas entradas ya se han gastado. Desliza hacia abajo para actualizar tu saldo e inténtalo de nuevo. | | |
| send.error.insufficient | Insufficient balance. You have {0} DGB available. | Saldo insuficiente. Tienes {0} DGB disponibles. | | |
| send.error.insufficient_with_fee | Insufficient balance for amount + network fee (~{0} DGB). You have {1} DGB available. | Saldo insuficiente para el importe más la comisión de red (~{0} DGB). Tienes {1} DGB disponibles. | | |
| send.error.invalid_address | Invalid DigiByte address. | Dirección de DigiByte no válida. | | |
| send.error.mempool_conflict | A conflicting transaction is already in the mempool. Please wait and try again. | Ya hay una transacción en conflicto en la mempool. Espera e inténtalo de nuevo. | | |
| send.error.not_address | That does not look like an address. | Eso no parece una dirección. | | |
| send.error.own_address | You cannot send to your own address. | No puedes enviar a tu propia dirección. | | |
| send.error.own_address_batch | Cannot send to your own address. | No puedes enviar a tu propia dirección. | | |
| send.slide.accessible | {0}. Slide, or hold Enter, to confirm. This cannot be undone. | {0}. Desliza, o mantén pulsado Intro, para confirmar. Esto no se puede deshacer. | | |
| send.slide.activate_again | Activate again to confirm | Activa de nuevo para confirmar | | |
| send.slide.announce_armed | Activate again within five seconds to confirm. | Activa de nuevo en cinco segundos para confirmar. | | |
| send.slide.announce_cancelled | Not confirmed. | No confirmado. | | |
| send.slide.announce_confirmed | Confirmed. {0} | Confirmado. {0} | | |
| send.unavailable | Sending is unavailable until the connection returns. Your funds are safe; try again in a moment. | No se puede enviar hasta que vuelva la conexión. Tus fondos están a salvo; inténtalo de nuevo en un momento. | | |
| send.unconfirmed_by_peers | Sent to the network. No peer has confirmed it yet; the wallet will keep watching. | Enviado a la red. Ningún nodo lo ha confirmado todavía; la billetera seguirá vigilándolo. | | |
| send.warn.below_minimum | Below minimum send amount (0.0001 DGB) | Por debajo del envío mínimo (0,0001 DGB) | | |
| send.warn.exceeds_balance | Exceeds available balance ({0} DGB) | Supera el saldo disponible ({0} DGB) | | |
| settings.backup | Back Up Seed Phrase | Copia de seguridad de la frase de recuperación | | |
| settings.backup.hint | View your recovery phrase | Ver tu frase de recuperación | | |
| settings.biometric.failed_or_cancelled | Biometric setup failed or was cancelled. | La configuración biométrica falló o se canceló. | | |
| settings.confirm_sends.hint | Require biometric before sending | Pedir biometría antes de enviar | | |
| settings.delete | Delete Wallet | Eliminar billetera | | |
| settings.delete.hint | Remove all wallet data from this device | Quita todos los datos de la billetera de este dispositivo | | |
| settings.delete.warning | This will permanently delete your wallet and all data. Make sure you have backed up your recovery phrase first. | Esto eliminará para siempre tu billetera y todos sus datos. Antes, asegúrate de tener una copia de seguridad de tu frase de recuperación. | | |
| settings.multisig.hint | For wallet {0} · send to co-signers (safe to share) | Para la billetera {0} · envíala a los cofirmantes (es seguro compartirla) | | |
| unlock.delete_forever | Delete Forever | Eliminar para siempre | | |
| unlock.delete_sub | This cannot be undone | Esto no se puede deshacer | | |
| unlock.delete_title | Delete wallet & start over | Eliminar la billetera y empezar de nuevo | | |
| unlock.delete_type_placeholder | Type DELETE to confirm | Ingresa DELETE para confirmar | | |
| unlock.delete_warning | This will permanently delete your wallet and all data. If you don't have your recovery phrase, your funds will be lost forever. | Esto eliminará de forma permanente tu billetera y todos sus datos. Si no tienes tu frase de recuperación, perderás tus fondos para siempre. | | |
| unlock.forgot_pin | Forgot PIN? | ¿Olvidaste el PIN? | | |
| unlock.new_pin | New PIN | Nuevo PIN | | |
| unlock.pin_prompt | Enter your 6-digit PIN | Ingresa tu PIN de 6 dígitos | | |
| unlock.recovery_options | Recovery Options | Opciones de recuperación | | |
| unlock.reset_enter_phrase | Enter your recovery phrase. | Ingresa tu frase de recuperación. | | |
| unlock.reset_failed | Could not verify. Check your recovery phrase. | No se pudo verificar. Revisa tu frase de recuperación. | | |
| unlock.reset_invalid | Invalid recovery phrase or private key. | Frase de recuperación o clave privada no válida. | | |
| unlock.reset_sub | Enter your seed phrase to set a new PIN or passphrase | Ingresa tu frase semilla para definir un nuevo PIN o frase de acceso | | |
| unlock.reset_title | Reset {0} with recovery phrase | Restablecer {0} con la frase de recuperación | | |
| unlock.seed_placeholder | Enter your 24-word recovery phrase... | Ingresa tu frase de recuperación de 24 palabras... | | |

When every row is approved: add "The Spanish recovery-phrase, PIN and send warnings were reviewed by a native speaker." to the **Spanish** entry under Unreleased in `CHANGELOG.md`, then mark T055 done in `tasks.md`.

**Wording to settle**: new text uses "billetera" (the word the file already used 42 times). Two older entries still say "Cartera": `app.name` = "Cartera DigiByte" and `nav.wallet` = "Cartera". Pick one word for the whole app and change those two if needed.
