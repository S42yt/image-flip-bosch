FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Backend/Backend.csproj Backend/
RUN dotnet restore Backend/Backend.csproj
COPY Backend/ Backend/
RUN dotnet publish Backend/Backend.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://0.0.0.0:80;http://0.0.0.0:5080
ENV Db=/data/memes.db
VOLUME /data
EXPOSE 80 5080
ENTRYPOINT ["dotnet", "image-flip-backend.dll"]
