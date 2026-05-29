# 1. Etap budowania (SDK zawiera kompilator)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Kopiujemy plik projektu i pobieramy biblioteki (Nugety)
COPY *.csproj ./
RUN dotnet restore

# Kopiujemy całą resztę plików źródłowych i publikujemy aplikację
COPY . ./
RUN dotnet publish -c Release -o out

# 2. Etap uruchomieniowy (Używamy obrazu ASPNET zamiast czystego DOTNET)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Kopiujemy skompilowaną aplikację z poprzedniego etapu
COPY --from=build-env /app/out .

# Wskazujemy punkt startowy kontenera
ENTRYPOINT ["dotnet", "meow.dll"]