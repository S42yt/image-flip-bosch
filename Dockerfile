FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY image-flip-bosch.csproj ./
RUN dotnet restore image-flip-bosch.csproj
COPY . .
RUN dotnet publish image-flip-bosch.csproj -c Release -o /app 

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://0.0.0.0:5080
ENV Db=/data/memes.db
VOLUME /data
EXPOSE 5080
ENTRYPOINT ["dotnet", "image-flip-bosch.dll", "serve"]
