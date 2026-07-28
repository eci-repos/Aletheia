FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Aletheia.Web/Aletheia.Web.csproj", "src/Aletheia.Web/"]
COPY ["src/Aletheia.Contracts/Aletheia.Contracts.csproj", "src/Aletheia.Contracts/"]
COPY ["src/Aletheia.Foundation/Aletheia.Foundation.csproj", "src/Aletheia.Foundation/"]
COPY ["src/Repository.Abstractions/Repository.Abstractions.csproj", "src/Repository.Abstractions/"]
COPY ["src/RAGS.Abstractions/RAGS.Abstractions.csproj", "src/RAGS.Abstractions/"]
COPY ["src/KnowledgeGraph.Abstractions/KnowledgeGraph.Abstractions.csproj", "src/KnowledgeGraph.Abstractions/"]
RUN dotnet restore "src/Aletheia.Web/Aletheia.Web.csproj"
COPY . .
WORKDIR "/src/src/Aletheia.Web"
RUN dotnet publish "Aletheia.Web.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY src/Aletheia.Web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
