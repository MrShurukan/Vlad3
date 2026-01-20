FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Vlad3.WebControl/Vlad3.WebControl.csproj", "Vlad3.WebControl/"]
RUN dotnet restore "Vlad3.WebControl/Vlad3.WebControl.csproj"
COPY . .
WORKDIR "/src/Vlad3.WebControl"
RUN dotnet build "./Vlad3.WebControl.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Vlad3.WebControl.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Vlad3.WebControl.dll"]
