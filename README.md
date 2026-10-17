# Calories Tracker

PWA Blazor WebAssembly (.NET 10) pour suivre mes calories : Claude estime le repas à partir d'une photo, je colle la ligne `MEAL|…` dans l'app, elle compare à ma cible.

## Structure

```
CaloriesTracker.slnx
src/CaloriesTracker.Core/        Logique pure, sans UI (testable)
  Models.cs                      Profile, WeightEntry, Meal, NutritionTargets
  NutritionCalculator.cs         Katch-McArdle, Mifflin-St Jeor, TDEE, macros
  MealLineParser.cs              Lecture / écriture des lignes MEAL|…
src/CaloriesTracker.Web/         PWA Blazor WASM
  Services/AppState.cs           État + persistance localStorage (à remplacer par Supabase plus tard)
  Pages/                         Aujourd'hui, Ajouter, Poids, Profil
tests/CaloriesTracker.Core.Tests/  xUnit
.github/workflows/deploy.yml     Tests + déploiement GitHub Pages
```

## Lancer en local (Mac)

1. Installer le [.NET 10 SDK](https://dotnet.microsoft.com/download) et, dans VS Code, l'extension **C# Dev Kit**.
2. Dans le dossier :
   ```bash
   dotnet test
   dotnet run --project src/CaloriesTracker.Web
   ```
3. Ouvrir l'URL affichée (ex. `http://localhost:5xxx`).

Pour tester sur l'iPhone avant le déploiement : `dotnet run --project src/CaloriesTracker.Web --urls http://0.0.0.0:5000`, puis ouvrir `http://<ip-du-mac>:5000` dans Safari (même Wi-Fi).

## Déployer gratuitement (GitHub Pages)

1. Repo GitHub **public** (exigé par Pages sur un compte gratuit) : Settings → General → Danger Zone → Change visibility.
2. Settings → **Pages** → Build and deployment → Source : **GitHub Actions**.
3. Pousser sur `main` (ou Actions → Build and deploy → Run workflow) : tests, publication, déploiement.
4. L'app est à `https://<utilisateur>.github.io/<repo>/`.

Le chemin de base (`/<repo>/`) est calculé à l'exécution dans `index.html` et le service worker, donc rien à configurer.

## Installer sur l'iPhone

Safari → l'URL GitHub Pages → **Partager** → **Sur l'écran d'accueil**. L'app s'ouvre en plein écran, hors ligne.

## Données

- Stockées dans le navigateur (localStorage), donc **par appareil**.
- Profil → **Exporter (JSON)** pour une sauvegarde ; **Importer** pour restaurer ou transférer.
- Prochaine étape possible : remplacer `AppState` par une synchro Supabase pour partager Mac ↔ iPhone.

## Format MEAL

```
MEAL|AAAA-MM-JJ|<repas>|description|kcal|prot g|gluc g|lip g
MEAL|2026-10-17|dejeuner|Arepa fromage, oeuf, café Natrel Plus|440|21|43|21
```

`<repas>` : `dejeuner`, `diner`, `souper` ou `collation`. On peut coller la réponse complète de Claude : seules les lignes qui commencent par `MEAL|` sont lues.
