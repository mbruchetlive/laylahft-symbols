# EPIC : Gestion des symboles de marché

## Objectif

Maintenir une liste fiable, à jour et structurée des symboles disponibles sur Binance (ou autre exchange), accessible en RAM pour toute la plateforme (API, front, moteur d’ordres, etc.).

---

## Livrables

- Service interne de téléchargement & parsing de symboles Binance
- Stockage structurant en mémoire (singleton)
- Endpoint interne `GET /internal/symbols`
- Task de rafraîchissement planifiée (ex: toutes les 12h)
- Mécanisme de failback si Binance est indisponible
- (Optionnel) Endpoint `POST /internal/symbols/refresh` (force refresh manuel)
- **Service de cache mémoire avec persistance automatique en tâche de fond**
- **Classe Event Dispatcher pour notifier les mises à jour de symboles**
- **Middleware d'autorisation sur les endpoints internes (clé API minimale pour POC)**

---

## User Stories

### US 1 : Téléchargement initial des symboles

**En tant que** DataAggregator, **je veux** récupérer la liste complète des symboles via l’API Binance, **afin de** disposer d’une base fiable en RAM dès le démarrage.

### US 2 : Rafraîchissement automatique

**En tant que** système, **je veux** mettre à jour les symboles toutes les X heures, **afin de** rester synchronisé avec les modifications de Binance.

### US 3 : Accès par d'autres services

**En tant que** service consommateur (API, front), **je veux** accéder aux symboles via un endpoint interne REST, **afin de** pouvoir afficher ou utiliser les données du marché.

### US 4 : Sérialisation en cache local

**En tant que** DataAggregator, **je veux** sauvegarder les symboles dans un fichier local, **afin de** pouvoir les recharger si l’API Binance devient indisponible.

### US 5 : Récupération en mode failback

**En tant que** DataAggregator, **je veux** lire les symboles depuis un cache local en cas d’échec, **afin de** continuer à servir des données aux services même sans accès réseau.

### US 6 : Journalisation explicite

**En tant que** opérateur, **je veux** voir dans les logs si le système fonctionne en mode failback, **afin de** réagir rapidement en cas de problème réseau/API.

### US 7 : Persistance auto du cache mémoire

**En tant que** DataAggregator, **je veux** que chaque changement dans le dictionnaire en mémoire déclenche une tâche de persistance, **afin de** garantir une sauvegarde continue et automatique.

### US 8 : Notification d'événements de mise à jour

**En tant que** module dépendant (API, WebSocket, moteur), **je veux** être notifié lorsqu’un symbole est ajouté ou mis à jour, **afin de** réagir en temps réel aux changements de données.

### US 9 : Sécurisation des endpoints

**En tant que** service interne, **je veux** qu’un middleware vérifie l’autorisation d’accès aux endpoints internes, **afin de** bloquer tout appel non authentifié (clé API minimale pour POC).

---

## Détails techniques du endpoint

### `GET /internal/symbols`

**Retourne** la liste complète des symboles avec paramètres de trading :

```json
[
  {
    "symbol": "BTCUSDT",
    "baseAsset": "BTC",
    "quoteAsset": "USDT",
    "pricePrecision": 2,
    "quantityPrecision": 6,
    "tickSize": "0.01",
    "stepSize": "0.000001",
    "minQty": "0.0001",
    "maxQty": "100.0",
    "minPrice": "10.0",
    "maxPrice": "1000000.0",
    "minNotional": "10.0",
    "filters": [ ... ]
  }
]
```

---

## Stratégie de failback

### Téléchargement

- Téléchargement via `https://api.binance.com/api/v3/exchangeInfo`
- Parsing JSON, filtre des symboles actifs, transform en structure interne

### Sauvegarde locale

- Fichier local : `symbols-cache.json`
- Mise à jour après chaque réussite de téléchargement

### Lecture fallback

- Si Binance échoue : lecture fichier `symbols-cache.json`
- Validation du cache : non vide, lisible, structuré

### Logs

- Succès = log info
- Fallback = log warning :

```
[WARN] Binance API failed. Loaded symbols from local cache.
```

- Erreur = log error + comportement dégradé si aucun cache valide n’existe

---

## Suivi Aspire

- Intégration d’un health check pour vérifier l’état de la source symboles
- Affichage dans le dashboard Aspire (status, dernier refresh, mode actif/fallback)

