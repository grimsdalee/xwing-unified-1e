# Architecture

Working principle:

```text
Unified engine
  ├── Global startup
  ├── Database loading
  ├── UI/browser
  ├── Squad importer
  ├── Ship spawner
  ├── Model database
  └── Asset manager

1E data layer
  ├── factions
  ├── ships
  ├── pilots
  ├── upgrades
  ├── conditions
  ├── damage decks
  ├── dials
  └── tokens
```

First task: reverse engineer the Unified 2.0/2.5 startup sequence.
