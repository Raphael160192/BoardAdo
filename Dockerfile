# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaura primeiro (aproveita cache de camadas do Docker)
COPY EntreQuiz.AdoMcp.csproj .
RUN dotnet restore

# Compila e publica
COPY . .
RUN dotnet publish EntreQuiz.AdoMcp.csproj -c Release -o /app

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# O Render injeta a porta real em $PORT em runtime; o Program.cs lê essa env var.
# O EXPOSE abaixo é apenas informativo.
EXPOSE 8080

ENTRYPOINT ["dotnet", "EntreQuiz.AdoMcp.dll"]
