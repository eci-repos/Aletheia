FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Repository.API/Repository.API.csproj", "src/Repository.API/"]
COPY ["src/Repository.Application/Repository.Application.csproj", "src/Repository.Application/"]
COPY ["src/Repository.Domain/Repository.Domain.csproj", "src/Repository.Domain/"]
COPY ["src/Repository.Abstractions/Repository.Abstractions.csproj", "src/Repository.Abstractions/"]
COPY ["src/Repository.Infrastructure.MinIO/Repository.Infrastructure.MinIO.csproj", "src/Repository.Infrastructure.MinIO/"]
COPY ["src/Repository.Infrastructure.PostgreSQL/Repository.Infrastructure.PostgreSQL.csproj", "src/Repository.Infrastructure.PostgreSQL/"]
COPY ["src/Aletheia.Foundation/Aletheia.Foundation.csproj", "src/Aletheia.Foundation/"]
COPY ["src/Aletheia.Security/Aletheia.Security.csproj", "src/Aletheia.Security/"]
COPY ["src/Aletheia.Contracts/Aletheia.Contracts.csproj", "src/Aletheia.Contracts/"]
COPY ["src/RAGS.Application/RAGS.Application.csproj", "src/RAGS.Application/"]
COPY ["src/RAGS.Abstractions/RAGS.Abstractions.csproj", "src/RAGS.Abstractions/"]
COPY ["src/RAGS.Infrastructure.PgVector/RAGS.Infrastructure.PgVector.csproj", "src/RAGS.Infrastructure.PgVector/"]
COPY ["src/RAGS.Infrastructure.PostgreSQL/RAGS.Infrastructure.PostgreSQL.csproj", "src/RAGS.Infrastructure.PostgreSQL/"]
COPY ["src/RAGS.Infrastructure.Graph/RAGS.Infrastructure.Graph.csproj", "src/RAGS.Infrastructure.Graph/"]
COPY ["src/KnowledgeGraph.Abstractions/KnowledgeGraph.Abstractions.csproj", "src/KnowledgeGraph.Abstractions/"]
COPY ["src/KnowledgeGraph.Application/KnowledgeGraph.Application.csproj", "src/KnowledgeGraph.Application/"]
COPY ["src/KnowledgeGraph.Infrastructure.Neo4j/KnowledgeGraph.Infrastructure.Neo4j.csproj", "src/KnowledgeGraph.Infrastructure.Neo4j/"]
RUN dotnet restore "src/Repository.API/Repository.API.csproj"
COPY . .
WORKDIR /src/src/Repository.API
RUN dotnet publish "Repository.API.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM build AS publish
RUN dotnet publish "Repository.API.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY docs/doc-templates /app/docs/doc-templates
ENTRYPOINT ["dotnet", "Repository.API.dll"]
