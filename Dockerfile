FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/JobRadar/JobRadar.csproj src/JobRadar/
RUN dotnet restore src/JobRadar/JobRadar.csproj
COPY src/JobRadar/ src/JobRadar/
RUN dotnet publish src/JobRadar/JobRadar.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /publish/ ./
RUN mkdir -p /app/reports && chown -R app:app /app/reports
USER app
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet","JobRadar.dll"]
