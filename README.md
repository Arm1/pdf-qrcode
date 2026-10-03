# Benchmark PDF -> Image en .NET 10

Ce projet compare les performances et les capacités de conversion de pages PDF en images sous **.NET 10** (Apple Silicon / macOS / Linux / Windows) avec :
1. **PDFiumCore** (Bindings directs C++ P/Invoke pour Google PDFium)
2. **PDFtoImage** (Wrapper moderne basé sur PDFiumCore + SkiaSharp)
3. **PDFsharp** (Explication et benchmark de l'extraction d'images XObject)

---

## 💡 Constats Clés & Architecture

1. **Docnet.Core vs PDFiumCore** :
   - `Docnet.Core` est effectivement ancien et n'est plus activement maintenu.
   - `PDFiumCore` (développé avec les builds officiels `bblanchon/pdfium-binaries`) est aujourd'hui le standard actif, moderne et synchronisé avec Google Chromium/PDFium.

2. **PDFsharp n'est PAS un rastériseur** :
   - `PDFsharp` (ou `PdfSharp.Pdf`) est une excellente bibliothèque pour **créer, fusionner, découper** ou manipuler des documents PDF vectoriels.
   - Elle ne contient **aucun moteur de rastérisation** de page : elle ne peut pas interpréter le texte, les polices et le vectoriel d'une page pour en faire un bitmap.
   - Elle permet en revanche d'**extraire les flux d'images bruts** (`/XObject /Image`) déjà embarqués dans la page.

---

## 🚀 Résultats du Benchmark (sur Apple Silicon M-series, 20 pages)

| Moteur / Méthode | Rôle Réel | Temps Total (20 p) | Moyenne / Page | Débit (Pages/s) | Mémoire Allouée (GC) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **PDFiumCore** | Rastérisation complète (P/Invoke) | **~24 ms** | **~1.2 ms** | **~830 p/s** | **0.00 Mo** (unmanaged) |
| **PDFtoImage** | Rastérisation complète (SkiaSharp) | **~29 ms** | **~1.4 ms** | **~686 p/s** | **0.01 Mo** |
| **PDFsharp** | Extraction brute d'images XObject | **~0.7 ms** | **~0.03 ms** | **~29 000 p/s** | **0.24 Mo** |

---

## 🛠️ Lancer le Benchmark

```bash
# Lancement avec génération automatique d'un PDF de test de 20 pages
dotnet run --project PdfImageBenchmark

# Ou tester sur votre propre fichier PDF
dotnet run --project PdfImageBenchmark -- /chemin/vers/mon_document.pdf
```
