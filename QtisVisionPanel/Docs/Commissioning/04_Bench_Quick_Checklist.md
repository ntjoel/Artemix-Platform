# Bench Quick Checklist

Questa checklist e' pensata per i primi 10-15 minuti al banco o in macchina.

Usala mentre:

- colleghi i connettori
- dai alimentazione
- apri il pannello la prima volta
- fai il primo sanity check prima del commissioning completo

Se qualcosa non torna, fermati e passa alle note dettagliate in:

- `03_First_Power_On_Sequence.md`
- `01_IO_Encoder_Commissioning_Plan.md`

---

## 1. Prima di collegare

Controlla:

- [ ] cavo I/O `PCL-10250-2E` disponibile
- [ ] terminal board I/O `ADAM-3951-BE` disponibile
- [ ] numero di `ADAM-3951-BE` previsto confermato
- [ ] cavo encoder `PCL-10137H-3E` disponibile
- [ ] terminal board encoder `ADAM-3937-BE` disponibile
- [ ] tipo encoder identificato (`single-ended` / `differenziale` / altro)
- [ ] tensione encoder confermata da datasheet
- [ ] schema segnali o tabella morsetti disponibile
- [ ] connettore I/O corretto identificato
- [ ] connettore encoder corretto identificato
- [ ] alimentazione disponibile e coerente
- [ ] GND/riferimento comune presenti dove richiesto
- [ ] nessun attuatore pericoloso lasciato libero di muoversi

---

## 2. Dopo il collegamento fisico

Controlla:

- [ ] `PCL-10250-2E` sul ramo I/O corretto
- [ ] `PCL-10137H-3E` sul ramo encoder corretto
- [ ] `ADAM-3951-BE` cablate sul lato corretto macchina
- [ ] `ADAM-3937-BE` cablata sul lato corretto macchina
- [ ] `A/B/Z` dell'encoder sul canale previsto
- [ ] `GND` encoder riferito correttamente a `PCIE-1884`
- [ ] connettori inseriti completamente
- [ ] nessun pin piegato o allentato
- [ ] cavi non in trazione
- [ ] morsetti serrati
- [ ] schermature presenti dove previste
- [ ] nessun corto evidente o filo scoperto

---

## 3. Prima alimentazione

Appena alimenti:

- [ ] nessun odore anomalo
- [ ] nessun rumore anomalo
- [ ] nessun led fuori atteso
- [ ] nessun movimento meccanico inatteso
- [ ] nessuna uscita sembra attivarsi da sola

Se una risposta e' negativa:

- [ ] STOP e togli alimentazione

---

## 4. Primo avvio software

Nel pannello controlla:

- [ ] top bar visibile
- [ ] `Machine Status` leggibile
- [ ] ricetta attiva leggibile
- [ ] sessione utente leggibile
- [ ] nessun crash all'avvio

---

## 5. Primo controllo eventi

Apri `Events Monitor` e verifica:

- [ ] nessun errore bloccante scheda I/O
- [ ] nessun errore bloccante encoder
- [ ] nessun errore runtime inspiegabile
- [ ] eventuali warning annotati

---

## 6. Rilevamento hardware

Conferma:

- [ ] `PCIE-1756` rilevata
- [ ] `PCIE-1884` rilevata se prevista
- [ ] nessuna disconnessione immediata

---

## 7. Stato segnali a riposo

Controlla:

- [ ] input `DI00-DI63` sensati a riposo
- [ ] input `DI100-DI103` sensati a riposo
- [ ] nessun ingresso fantasma evidente
- [ ] nessuna fluttuazione continua

---

## 8. Encoder a fermo

Controlla:

- [ ] `ENC1-ENC4` leggibili se presenti
- [ ] valore stabile a macchina ferma
- [ ] nessuna deriva spontanea
- [ ] primo test eseguito su `Counter0 / ENC1`

---

## 9. Mini prova sicura

Esegui solo una prova sicura:

- [ ] un input manuale noto cambia sul canale corretto
- [ ] oppure un piccolo movimento encoder produce conteggio sensato

Non fare ancora:

- [ ] test espulsione reale
- [ ] test output su carichi critici
- [ ] test trigger completo

---

## 10. Esito rapido

### GO

Puoi passare al commissioning completo se:

- [ ] software stabile
- [ ] hardware rilevato
- [ ] input a riposo credibili
- [ ] encoder stabile a fermo

### NO-GO

Fermati se trovi:

- [ ] output inattesi
- [ ] input incoerenti
- [ ] encoder instabile
- [ ] errori continui nei log
- [ ] freeze UI o riavvii

---

## 11. Cosa segnare subito

Annota prima di andare oltre:

- [ ] data e ora
- [ ] tecnico presente
- [ ] ricetta attiva
- [ ] schede rilevate
- [ ] stato macchina iniziale
- [ ] esito `GO` / `NO-GO`
- [ ] eventuali anomalie immediate

---

## Passo successivo

Se l'esito e' buono:

1. apri `03_First_Power_On_Sequence.md` se devi ancora consolidare la prima accensione
2. poi passa a `01_IO_Encoder_Commissioning_Plan.md`
3. compila `02_Commissioning_Test_Sheet.md`
