# Encoder Integration Template

## Scopo

L'encoder di trasporto serve a:

- misurare la velocita` reale del nastro
- stimare la posizione istantanea del prodotto
- applicare trigger, acquisizione e scarto in modo coerente con il movimento reale

## Variabili da definire

- encoder principale: `ENC_CONVEYOR_MAIN`
- canale fisico: `PCIE-1884-AE / Counter0`
- modalita`: quadratura `x4`
- impulsi per giro
- sviluppo meccanico per giro in mm
- fattore `mm/pulse`
- offset fotocellula -> trigger camera
- offset fotocellula -> punto scarto
- velocita` minima valida
