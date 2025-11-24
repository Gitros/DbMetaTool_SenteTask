DbMetaTool � narz�dzie do eksportu i odtwarzania metadanych Firebird 5.0
==========================================================================

Opis
----
`DbMetaTool` to aplikacja konsolowa napisana w .NET 8, s�u��ca do:

- budowania bazy Firebird 5.0 ze skrypt�w SQL,
- generowania skrypt�w metadanych z istniej�cej bazy (eksport do SQL),
- aktualizacji istniej�cej bazy na podstawie katalogu ze skryptami.

Aplikacja obs�uguje w uproszczonym zakresie tylko: domeny, tabele (z kolumnami) oraz procedury.

Wymagania
---------
- .NET 8 SDK
- Serwer Firebird 5.0 (lokalnie lub zdalnie) � zainstalowany i uruchomiony
- (opcjonalnie) IBExpert lub inny klient do r�cznej weryfikacji bazy

Budowanie
---------
W katalogu z rozwi�zaniem uruchom:

```
dotnet restore
dotnet build
```

Testy jednostkowe (je�li do��czone):

```
dotnet test ./DbMetaTool.Tests/DbMetaTool.Tests.csproj
```

U�ycie
------
Aplikacja ma trzy g��wne polecenia:

1) `build-db` � zbuduj now� baz� ze skrypt�w

Parametry:
- `--db-dir <�cie�ka>` � katalog, w kt�rym zostanie utworzony plik bazy (`.fdb`)
- `--scripts-dir <�cie�ka>` � katalog zawieraj�cy skrypty `.sql`

Przyk�ad:

```
DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
```

2) `export-scripts` � wygeneruj skrypty z istniej�cej bazy

Parametry:
- `--connection-string "<connStr>"` � connection string do bazy
- `--output-dir <�cie�ka>` � katalog wyj�ciowy dla plik�w

Przyk�ad:

```
DbMetaTool export-scripts --connection-string "User=SYSDBA;Password=masterkey;Database=C:\db\fb5\database.fdb;DataSource=localhost;Port=3050;Charset=UTF8" --output-dir "C:\out"
```

3) `update-db` � zaktualizuj istniej�c� baz� na podstawie skrypt�w

Parametry:
- `--connection-string "<connStr>"`
- `--scripts-dir <�cie�ka>`

Przyk�ad:

```
DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
```

Jak to dzia�a (kr�tko)
----------------------
- `Program.cs` � parsowanie argument�w i wywo�ania operacji.
- `DatabaseManager` � tworzy baz� (`CreateDatabase`), wykonuje skrypty i aktualizuje istniej�c� baz�.
- `ScriptExporter` � odczytuje metadane przez `IDatabaseMetadataReader` i zapisuje pliki SQL w katalogach `domains`, `tables`, `procedures`.
- `FileManager` � zapisuje pliki na dysku.

Ograniczenia i znane braki
-------------------------
- Eksport w formacie `SQL` jest zaimplementowany. Format�w `JSON` i `TXT` nie zaimplementowano (rzucaj� `NotImplementedException`).
- Obs�uga obiekt�w takich jak constraints, indeksy, triggery itp. jest pomini�ta (zgodnie z uproszczonym zakresem zadania).
- Operacja `update-db` dzia�a permisywnie: pomija istniej�ce domeny i tabele (nie modyfikuje ich), natomiast wykonuje procedury i pozosta�e skrypty.

Weryfikacja poprawno�ci dzia�ania
---------------------------------
Sugerowany scenariusz r�cznej weryfikacji:
1. Utw�rz manualnie baz� testow� z kilkoma domenami, tabelami i procedurami.
2. Uruchom `export-scripts` i zapisz wygenerowane pliki.
3. U�yj `build-db`, wskazuj�c inny katalog docelowy oraz katalog wygenerowanych skrypt�w.
4. Por�wnaj struktury obu baz (domeny, tabele, procedury).

Sugestie rozwoju
-----------------
- Implementacja eksportu do `JSON` i `TXT`.
- Obs�uga constraints, indeks�w i trigger�w.
- Mechanizm �diff� schemat�w i migracji (ALTER / patch).
- Transakcje i rollback podczas wykonywania skrypt�w.

