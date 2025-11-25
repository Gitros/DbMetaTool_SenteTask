
# DbMetaTool – narzędzie do eksportu i odtwarzania metadanych Firebird 5.0
--------------------------------------------------------------------------

## 📌 Opis

**DbMetaTool** to aplikacja konsolowa napisana w **.NET 8**, służąca do:

- budowania nowej bazy Firebird 5.0 ze skryptów SQL,
- generowania skryptów metadanych z istniejącej bazy (eksport do SQL),
- aktualizacji istniejącej bazy na podstawie katalogu ze skryptami.

Aplikacja obsługuje w uproszczonym zakresie **tylko trzy typy obiektów**:

✔ domeny  
✔ tabele (z kolumnami)  
✔ procedury  

Pozostałe obiekty (constraints, indeksy, triggery itp.) są **pominięte**, zgodnie z wymaganiami zadania.

---

## ⚙️ Wymagania

- **.NET 8 SDK**
- **Firebird Server 5.0** (zainstalowany lokalnie lub zdalnie)
- (opcjonalnie) **IBExpert**, **DBeaver** lub inny klient bazodanowy

---

## 🧱 Budowanie projektu

W katalogu głównym repozytorium:

```bash
dotnet restore
dotnet build
```

---

## 🧪 Testy jednostkowe

Jeśli projekt testów jest dołączony:

```bash
dotnet test ./DbMetaTool.Tests/DbMetaTool.Tests.csproj
```

Testy działają na **tymczasowych katalogach**, tworzą osobne pliki `.fdb` i nie wymagają ręcznego czyszczenia.

---

# 🚀 Użycie

Aplikacja działa z poziomu terminala i obsługuje trzy główne komendy.

---

## 1) **build-db** — zbuduj bazę ze skryptów

Parametry:

| parametr        | opis |
|-----------------|------|
| `--db-dir`      | katalog, w którym ma zostać utworzona baza `.fdb` |
| `--scripts-dir` | katalog zawierający skrypty SQL |

Przykład:

```bash
DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
```

---

## 2) **export-scripts** — wygeneruj skrypty z istniejącej bazy

Parametry:

| parametr              | opis |
|-----------------------|------|
| `--connection-string` | connection string do bazy Firebird |
| `--output-dir`        | katalog, do którego zostaną zapisane pliki |

Przykład:

```bash
DbMetaTool export-scripts --connection-string "User=SYSDBA;Password=masterkey;Database=C:\db\fb5\database.fdb;DataSource=localhost;Port=3050;Charset=UTF8" --output-dir "C:\out"
```

Rezultat to struktura:

```
out/
 ├── domains/
 ├── tables/
 └── procedures/
```

---

## 3) **update-db** — zaktualizuj bazę na podstawie katalogu skryptów

Parametry:

| parametr              | opis |
|-----------------------|------|
| `--connection-string` | connection string do istniejącej bazy |
| `--scripts-dir`       | katalog ze skryptami SQL |

Przykład:

```bash
DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
```

---

# 📂 Struktura katalogów skryptów

DbMetaTool oczekuje następującej struktury:

```
scripts/
 ├── domains/
 │    ├── D_PRICE.sql
 │    └── D_NAME.sql
 ├── tables/
 │    ├── ITEMS.sql
 │    └── TAGS.sql
 └── procedures/
      ├── P_GET_ITEMS.sql
      └── P_LOG_CHANGE.sql
```

Każdy plik odpowiada jednemu obiektowi Firebird.

---

# 📐 Architektura projektu

```
DbMetaTool/
 ├── Application/
 │    ├── Contracts/        → interfejsy (IDatabaseManager, IScriptExporter...)
 │    └── Services/         → implementacje logiki
 ├── Domain/
 │    └── Models/           → POCO: DomainType, Table, Column, Procedure
 ├── Infrastructure/        → pomocnicze narzędzia (np. FileSaver)
 ├── Program.cs             → parsowanie argumentów i uruchamianie operacji
```

---

# 🔄 Działanie update-db

`update-db` działa w sposób **bezpieczny / permisywny**:

| Obiekt       | Zachowanie |
|--------------|------------|
| **domena**   | tworzona, jeśli nie istnieje |
| **tabela**   | tworzona, jeśli nie istnieje |
| **procedura**| zawsze wykonywana (`CREATE OR ALTER`) |
| **inne pliki** | wykonywane bez zmian |

---

# 🧪 Zakres testów (DbMetaTool.Tests)

Testy integracyjne sprawdzają:

✔ tworzenie bazy z katalogu skryptów  
✔ eksport skryptów z istniejącej bazy  
✔ idempotentność update-db  
✔ propagację nowych obiektów  
✔ brak usuwania istniejących kolumn  

---

# 🔮 Sugestie rozwoju

- eksport JSON / TXT  
- obsługa triggerów, indeksów, constraints  
- generator migracji (ALTER TABLE)  
- automatyczny schema diff  
- rollback przy błędach  

