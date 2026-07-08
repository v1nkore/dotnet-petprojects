# Multi-stage: SDK-образ собирает, лёгкий aspnet-образ (без SDK) исполняет.
# Итоговый образ ~220 МБ вместо ~900, и в проде нет компилятора — меньше поверхность атаки.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Сначала только csproj + restore: этот слой кэшируется, пока не меняются зависимости
COPY src/AspNetLab.Api/AspNetLab.Api.csproj src/AspNetLab.Api/
RUN dotnet restore src/AspNetLab.Api
COPY src/ src/
RUN dotnet publish src/AspNetLab.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# aspnet-образ слушает 8080 и работает НЕ от root (user 'app') — дефолты с .NET 8
EXPOSE 8080
ENTRYPOINT ["dotnet", "AspNetLab.Api.dll"]
