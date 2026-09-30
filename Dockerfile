FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DbMcp.csproj ./
RUN dotnet restore DbMcp.csproj -r linux-x64
COPY Configuration/ Configuration/
COPY Data/ Data/
COPY Tools/ Tools/
COPY Program.cs ./
RUN dotnet publish DbMcp.csproj -c Release -r linux-x64 --no-restore --self-contained false -p:PublishSingleFile=false -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "DbMcp.dll"]