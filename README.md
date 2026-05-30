Instrukcja uruchomienia projektu
Aby uruchomić system na dowolnym komputerze, wymagane jest posiadanie zainstalowanego
oprogramowania Docker Desktop.

Krok 1: Wyłączenie lokalnych serwerów
Upewnij się, że lokalne programy typu XAMPP, WampServer, MAMP czy systemowe usługi MySQL są
całkowicie wyłączone, aby zwolnić porty sieciowe.

Krok 2: Uruchomienie terminala
Otwórz terminal systemowy (PowerShell na Windows lub Terminal na macOS) i przejdź do katalogu
głównego projektu (tam gdzie znajduje się plik docker-compose.yml )

Krok 3: Czyszczenie środowiska (opcjonalnie, zalecane przy błędach)
Jeśli w systemie znajdują się stare lub zawieszone kontenery, wykonaj twarde czyszczenie pamięci
podręcznej i wolumenów:

```bash
docker-compose down --volumes --remove-orphans
```


Krok 4: Uruchomienie kontenerów
Wpisz komendę, która wymusi pobranie obrazów, kompilację kodu C# i start całego systemu:
```bash
docker-compose up --build
```
Poczekaj, aż w oknie konsoli przestaną lecieć logi startowe, Entity Framework Core automatycznie zaaplikuje
wszystkie zaległe migracje bazodanowe i pojawi się zielona ikona oraz komunikat sygnalizacyjny:
🐾 Baza danych gotowa.

Krok 5: Otwarcie aplikacji w przeglądarce
Otwórz dowolną przeglądarkę internetową i przejdź pod dedykowany adres internetowy (port 8000 został
dobrany tak, aby nie powodować konfliktów z systemową usługą AirPlay na komputerach Mac):
http://localhost:8000
Krok 6: Dane logowania administratora
W systemie zaimplementowano mechanizm automatycznego seedowania konta administratora przy
pierwszym uruchomieniu bazy danych. Zaloguj się w prawym górnym rogu na dedykowane konto
deweloperskie:
•
Login: superadmin
•
Hasło: admin123
