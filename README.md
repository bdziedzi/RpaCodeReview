# RpaCodeReview

Lokalny, deterministyczny proces UiPath do przeglądu innych projektów RPA.

## Prywatność i bezpieczeństwo

- Analizator czyta wyłącznie pliki z `ProjectPath` oraz wskazany `RulesPath`.
- Nie wywołuje modeli AI, Internetu, UiPath Cloud, Orchestratora ani żadnych zewnętrznych endpointów.
- Nie wysyła zawartości projektu poza komputer.
- Wynik każdej reguły ma status dokładnie `Pass`, `Fail` albo `Warning`.
- Dowód zawiera ścieżkę względną, numer linii i krótki cytat. Nie zapisuj raportu w miejscu publicznie współdzielonym, jeśli kod zawiera dane wrażliwe.

## Argumenty wejściowe

- `ProjectPath` — wymagany; lokalna ścieżka do analizowanego projektu UiPath.
- `RulesPath` — opcjonalny; domyślnie `Rules.json` obok procesu.
- `OutputReportPath` — opcjonalny; domyślnie `RpaCodeReviewReport.xlsx` w analizowanym projekcie.
- `IncludeHiddenFiles` — opcjonalny Boolean; domyślnie `False`.
- `FailOnMissingRequiredArtifact` — opcjonalny Boolean; gdy `True`, brak opcjonalnego artefaktu traktowany jest jako `Fail`.
- `RuleFilter` — opcjonalny tekst; filtruje po identyfikatorze lub kategorii reguły.

## Raport

`RpaCodeReviewReport.xlsx` ma arkusze:

1. `Summary` — ścieżki, czas, liczby wyników i informacja o lokalnym trybie pracy.
2. `RuleResults` — jeden wiersz na regułę: status, uzasadnienie, ważność i rekomendacja.
3. `Evidence` — dowody: plik, linia, cytat i szczegóły.
4. `ProjectInventory` — inwentarz odczytanych plików.

## Interpretacja

- `Pass` — znaleziono dowód spełnienia warunku.
- `Fail` — reguła wymagana nie została potwierdzona albo znaleziono naruszenie.
- `Warning` — kontrola wymaga decyzji lub weryfikacji poza kodem, np. historia Git, konfiguracja Orchestratora, wykonanie Unattended albo ocena architektoniczna.

## Uruchomienie

W UiPath Studio/Assistant wybierz workflow `RpaCodeReviewWorkflow` i przekaż argument `ProjectPath`. Przykładowe wywołanie lokalne z linii poleceń po zbudowaniu projektu:

```text
uip rpa run "C:\\Users\\bdziedzi\\Documents\\UiPath\\RpaCodeReview" --entry-point RpaCodeReviewWorkflow.cs --input-arguments ProjectPath="C:\\Scanned\\MyBot"
```

Reguły można rozszerzać w `Rules.json` bez modyfikowania kodu. Dla kontroli zależnych od kontekstu biznesowego należy użyć `checkType: "Manual"` i opisać `manualReason`.

### Status implementacji reguły

Katalog zawiera także metadane oceny pokrycia implementacyjnego. Nie są one tym samym co runtime'owy status `Pass`, `Fail` albo `Warning`:

- `Pełna` — wszystkie wymagania atomowej reguły mogą być sprawdzone lokalnie i deterministycznie;
- `Częściowa` — analizator potwierdza tylko część wymagania, a pozostała część wymaga kontekstu lub ręcznej oceny;
- `Brak / do weryfikacji ręcznej` — reguła zależy głównie od Orchestratora, Git, runtime, dokumentów biznesowych albo decyzji architektonicznej.

Pola `evidenceScope`, `limitations` i `recommendedAction` opisują odpowiednio zakres dowodu, ograniczenie kontroli i następny krok. `1.1.2` jest regułą grupującą i nie zastępuje atomowych kontroli `1.1.2.1`–`1.1.2.5`. Kontrola `1.1.2.4` pozostaje ostrzeżeniem do oceny słownika domenowego; nie próbuje automatycznie rozstrzygać, czy skrót jest łatwy do skojarzenia.

### Jawne typy kontroli

- `RuleGroup` — informacyjna grupa nadrzędna; szczegółowy wynik pochodzi z podreguł.
- `VariableNotation` — sprawdza rozpoznane deklaracje pod kątem PascalCase, camelCase lub snake_case; nie ocenia znaczenia nazwy.
- `ForbiddenNameToken` — wykrywa samodzielny człon `i`, a nie każdą literę `i` w wyrazie.
- `TypedVariablePrefix` — wiąże rozpoznany typ `DataTable` z prefiksem `dt`.
- `AbbreviationReview` — zawsze zwraca `Warning`, ponieważ skojarzenie skrótu wymaga kontekstu domenowego.
- `GlobalVariablePrefix` — sprawdza prefiks `g_`/`global_` tylko dla rozpoznanego scope globalnego; brak rozpoznanego scope nie jest udawany jako `Pass`.
- `ArgumentNaming` — sprawdza deklaracje argumentów w obsługiwanych postaciach XAML i C# pod kątem `in_`, `out_` oraz `io_`.

Kontrole parserowe mogą zwrócić `Warning`, gdy nie znaleziono materiału wejściowego albo nie da się jednoznacznie rozpoznać konstrukcji. To celowe zachowanie: brak dowodu nie jest równoznaczny z potwierdzeniem zgodności.


## Najważniejsze artefakty:

RpaCodeReviewWorkflow.cs — główny workflow z argumentem ProjectPath.
Rules.json — konfigurowalny katalog 143 atomowych reguł i podreguł.
LocalProjectReviewEngine.cs — lokalna analiza XAML, C#, JSON, manifestów i plików projektu.
LocalXlsxReportWriter.cs — generator raportu XLSX.
ReviewModels.cs — modele reguł, dowodów i wyników.
project.json — konfiguracja entry pointu i argumentów.
README.md — instrukcja uruchomienia i interpretacji wyników.
SmokeTestReport.xlsx — przykładowy raport z testu.
Proces:

przyjmuje ścieżkę analizowanego projektu jako argument ProjectPath,
obsługuje opcjonalne RulesPath, OutputReportPath, IncludeHiddenFiles, FailOnMissingRequiredArtifact i RuleFilter,
działa lokalnie i deterministycznie,
nie wysyła kodu do AI, UiPath Cloud, Orchestratora ani żadnego zewnętrznego endpointu,
dla każdej reguły zwraca wyłącznie Pass, Fail albo Warning,
zapisuje dowody: plik, numer linii i cytat,
generuje arkusze Summary, RuleResults, Evidence i ProjectInventory.

## Szczegółowy opis argumentów `RpaCodeReviewWorkflow`

Workflow ma sześć argumentów wejściowych oraz jeden argument wyjściowy.

| Argument | Typ | Wymagany | Wartość domyślna | Znaczenie |
|---|---|---:|---|---|
| `ProjectPath` | `String` | Tak | brak | Lokalna ścieżka do analizowanego projektu UiPath. |
| `RulesPath` | `String` | Nie | `Rules.json` obok procesu | Ścieżka do pliku z konfiguracją reguł. |
| `OutputReportPath` | `String` | Nie | `RpaCodeReviewReport.xlsx` w analizowanym projekcie | Lokalizacja raportu XLSX. |
| `IncludeHiddenFiles` | `Boolean` | Nie | `False` | Czy analizować ukryte pliki. |
| `FailOnMissingRequiredArtifact` | `Boolean` | Nie | `False` | Czy brak opcjonalnego artefaktu ma być oznaczony jako `Fail`, a nie `Warning`. |
| `RuleFilter` | `String` | Nie | pusty tekst | Ograniczenie analizy do identyfikatora lub kategorii reguły. |

Argument wyjściowy:

| Argument | Typ | Znaczenie |
|---|---|---|
| `Output` | `String` | Pełna ścieżka do wygenerowanego raportu XLSX. |

### `ProjectPath`

Ścieżka do lokalnego katalogu projektu UiPath, który ma zostać przeanalizowany. Proces odczytuje między innymi `project.json`, pliki `.xaml`, `.cs`, `.json`, `README.md`, pliki konfiguracyjne oraz katalogi takie jak `ExceptionScreenshots` i `Input-data-TST`.

Przykładowe wartości:

```text
C:\Projects\InvoiceBot
```

```text
D:\RPA\Production\CustomerProcess
```

```text
C:\Users\bdziedzi\Documents\UiPath\MyBot
```

To jedyny wymagany argument. Jeżeli katalog nie istnieje, proces zakończy się błędem walidacji wejścia.

### `RulesPath`

Ścieżka do pliku `Rules.json` zawierającego reguły code review. Jeżeli argument pozostanie pusty, proces spróbuje użyć pliku `Rules.json` w katalogu procesu. Zalecane jest przekazywanie pełnej ścieżki, szczególnie przy uruchomieniu opublikowanego pakietu.

Przykładowe wartości:

```text
C:\Users\bdziedzi\Documents\UiPath\RpaCodeReview\Rules.json
```

```text
D:\RPA\ReviewConfiguration\Rules-production.json
```

```text
C:\Projects\CodeReview\Configurations\Rules-security-only.json
```

Plik reguł zawiera między innymi identyfikator, kategorię, opis, typ kontroli, wzorce, ważność i zasady klasyfikacji wyniku.

### `OutputReportPath`

Pełna ścieżka, pod którą zostanie zapisany raport XLSX. Jeśli argument jest pusty, raport zostanie zapisany jako `RpaCodeReviewReport.xlsx` w katalogu wskazanym przez `ProjectPath`. Katalog docelowy jest tworzony automatycznie, jeżeli jeszcze nie istnieje.

Przykładowe wartości:

```text
C:\Projects\InvoiceBot\Reports\InvoiceBot_CodeReview.xlsx
```

```text
D:\RPA\ReviewResults\CustomerProcess_2026-10-12.xlsx
```

```text
C:\Users\bdziedzi\Documents\RPA\Reports\LatestReview.xlsx
```

Raport zawiera arkusze `Summary`, `RuleResults`, `Evidence` i `ProjectInventory`.

### `IncludeHiddenFiles`

Argument typu `Boolean`: `True` albo `False`. Określa, czy analizator ma odczytywać ukryte pliki. Domyślnie ma wartość `False`.

- `False` — zalecane ustawienie standardowego code review; ukryte pliki są pomijane.
- `True` — przydatne, gdy projekt zawiera istotną konfigurację w ukrytym pliku.

Katalogi techniczne, takie jak `.local`, `.git`, `bin`, `obj`, `.project`, `.vs` i `packages`, są pomijane niezależnie od tej opcji.

### `FailOnMissingRequiredArtifact`

Argument typu `Boolean`. Reguły oznaczone jako `required: true` zwracają `Fail`, jeśli nie zostaną spełnione. Ta opcja dodatkowo wpływa na reguły, które nie są obowiązkowe:

- `False` — brak opcjonalnego artefaktu daje `Warning`.
- `True` — brak opcjonalnego artefaktu daje `Fail`.

Przykład: brak katalogu `Input-data-TST` przy regule `required: false` daje `Warning` dla `False` oraz `Fail` dla `True`.

### `RuleFilter`

Tekstowy filtr reguł. Gdy jest pusty, analizowane są wszystkie reguły. Filtr działa po identyfikatorze oraz kategorii reguły i nie jest wyrażeniem regularnym.

Przykładowe wartości:

```text
Security
```

Uruchamia reguły z kategorii bezpieczeństwa, np. `Security.1`.

```text
4.5
```

Uruchamia reguły dotyczące konfiguracji, np. `4.5`, `4.5.queue` i `4.5.environment`.

```text
```

Pusty tekst uruchamia kompletne code review.

## Trzy przykłady konfiguracji argumentów

### Przykład 1 — pełny standardowy przegląd

```json
{
  "ProjectPath": "C:\\Projects\\InvoiceBot",
  "RulesPath": "C:\\Users\\bdziedzi\\Documents\\UiPath\\RpaCodeReview\\Rules.json",
  "OutputReportPath": "C:\\Projects\\InvoiceBot\\Reports\\InvoiceBot_CodeReview.xlsx",
  "IncludeHiddenFiles": false,
  "FailOnMissingRequiredArtifact": false,
  "RuleFilter": ""
}
```

Efekt: analizowany jest cały projekt, używany jest pełny katalog reguł, opcjonalne braki dają `Warning`, a raport jest zapisany w osobnym katalogu `Reports`.

### Przykład 2 — rygorystyczny audyt z ukrytymi plikami

```json
{
  "ProjectPath": "D:\\RPA\\CustomerProcess",
  "RulesPath": "D:\\RPA\\ReviewConfiguration\\Rules-production.json",
  "OutputReportPath": "D:\\RPA\\AuditResults\\CustomerProcess_Audit.xlsx",
  "IncludeHiddenFiles": true,
  "FailOnMissingRequiredArtifact": true,
  "RuleFilter": ""
}
```

Efekt: analizowane są również ukryte pliki, używana jest produkcyjna konfiguracja reguł, brak opcjonalnych artefaktów może dać `Fail`, a raport trafia do `D:\\RPA\\AuditResults`.

### Przykład 3 — tylko kontrola bezpieczeństwa

```json
{
  "ProjectPath": "C:\\Projects\\PaymentBot",
  "RulesPath": "C:\\Projects\\CodeReview\\Rules.json",
  "OutputReportPath": "C:\\Projects\\PaymentBot\\Reports\\PaymentBot_Security.xlsx",
  "IncludeHiddenFiles": false,
  "FailOnMissingRequiredArtifact": true,
  "RuleFilter": "Security"
}
```

Efekt: analizowane są wyłącznie reguły zawierające `Security` w identyfikatorze lub kategorii, np. reguła dotycząca jawnych haseł i kluczy. Brak wymaganych dowodów daje `Fail`, a raport zawiera tylko wyniki tego wycinka analizy.

## Ważna uwaga dotycząca zakresu analizy

Proces wyklucza z analizy plik `Rules.json` oraz plik wynikowego raportu, jeżeli znajdują się w analizowanym katalogu. Dzięki temu wzorce zapisane w konfiguracji reguł nie są błędnie interpretowane jako kod badanego projektu.

Wartości Boolean w UiPath Studio należy przekazywać jako `True` albo `False`, a nie jako tekst w cudzysłowie.
