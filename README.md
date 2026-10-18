# Calories Tracker

PWA Blazor WebAssembly (.NET 10) pour suivre mes calories : Claude estime le repas à partir d'une photo, je colle la ligne `MEAL|…` dans l'app, elle compare à ma cible.

## Structure

```
CaloriesTracker.slnx
src/CaloriesTracker.Core/        Logique pure, sans UI (testable)
  Models.cs                      Profile, WeightEntry, Meal, NutritionTargets
  NutritionCalculator.cs         Katch-McArdle, Mifflin-St Jeor, TDEE, macros
  MealLineParser.cs              Lecture / écriture des lignes MEAL|…
  SyncResolver.cs                Règle de synchro (dernière modification gagne)
  FoodCatalog.cs                 Table des aliments + favoris (aliments.json)
src/CaloriesTracker.Web/         PWA Blazor WASM
  Services/AppState.cs           État, localStorage + synchro
  Services/SupabaseClient.cs     Auth par code + table user_data (API REST)
  Pages/                         Aujourd'hui, Ajouter, Poids, Profil
tests/CaloriesTracker.Core.Tests/  xUnit
.github/workflows/deploy.yml     Tests + déploiement GitHub Pages
supabase/setup.sql               Table + règles d'accès Supabase
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

## Données et synchro

- Toujours enregistrées d'abord dans le navigateur (localStorage) : l'app marche hors ligne.
- Avec Supabase configuré et une connexion (Profil → Synchronisation, courriel + mot de passe), le même document est synchronisé entre appareils :
  - au démarrage, après chaque modification, et quand l'app revient au premier plan ;
  - règle « dernière modification gagne » ; à la première connexion d'un appareil, la copie du nuage gagne si elle existe.
- Profil → **Exporter (JSON)** reste disponible comme copie de secours.

### Configurer Supabase

1. Créer un projet sur supabase.com (plan Free).
2. SQL Editor → exécuter `supabase/setup.sql` (table `user_data` + règles RLS : chacun ne voit que sa ligne).
3. **Connexion recommandée : mot de passe.** Authentication → Users → **Add user → Create new user** (courriel + mot de passe, **Auto Confirm User** coché), puis Authentication → Sign In / Providers → décocher **Allow new users to sign up**. Aucun courriel n'est envoyé, donc pas de quota.
   Option de secours, lien par courriel : Authentication → URL Configuration : **Site URL** = l'adresse GitHub Pages de l'app (et l'ajouter aux Redirect URLs, avec `http://localhost:5260/`).
   Connexion : l'app accepte le code à 6 chiffres (si le modèle de courriel contient `{{ .Token }}`, ce qui exige un SMTP perso sur le plan gratuit), **le lien du courriel collé sans l'ouvrir**, ou l'adresse obtenue après l'avoir ouvert. Sur l'ordi, cliquer le lien connecte directement l'app ; sur l'iPhone (app d'écran d'accueil, stockage séparé de Safari), on colle le lien dans Profil → Synchronisation.
4. Remplir `src/CaloriesTracker.Web/wwwroot/appsettings.json` avec la Project URL et la clé **anon / publishable** (publiques par conception). Ne jamais y mettre la clé service_role / secret.

## Table des aliments (`aliments.json`)

`src/CaloriesTracker.Web/wwwroot/aliments.json` contient les valeurs **des étiquettes** des aliments habituels (par portion indiquée), et des **favoris** (combinaisons, ex. café + 250 ml de lait + stevia).
- Dans l'app : onglet Ajouter → **Favoris** → choisir, ajuster la quantité (× 0,5), Ajouter.
- Pour Claude : le même fichier est ajouté au projet, pour qu'il utilise ces valeurs exactes.
- Pour ajouter un aliment : copier un bloc, changer `id` (unique), `nom`, `portion` et les valeurs, puis commit/push (un test vérifie que le fichier reste valide).

## Format MEAL

```
MEAL|AAAA-MM-JJ|<repas>|description|kcal|prot g|gluc g|lip g
MEAL|2026-10-17|dejeuner|Rôties, oeufs, café|400|20|40|18
```

`<repas>` : `dejeuner`, `diner`, `souper` ou `collation`. On peut coller la réponse complète de Claude : seules les lignes qui commencent par `MEAL|` sont lues.
