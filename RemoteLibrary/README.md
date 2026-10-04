# Publicação da biblioteca de famílias

Este diretório define o formato da biblioteca que o botão **Baixar biblioteca** usa.

1. Gere pacotes `.zip` contendo somente arquivos `.rfa`, preservando as pastas de categoria e subcategoria.
2. Publique os pacotes em uma Release pública do GitHub.
3. Publique `catalogo-bibliotecas.json` na mesma Release.
4. Cada pacote deve informar tamanho e SHA-256 no catálogo.
5. Atualize `Core/LibraryDownload.cs` com o endereço HTTPS do catálogo antes de gerar o instalador.

O catálogo deve seguir este formato:

```json
{
  "name": "Biblioteca de Famílias BIM",
  "version": "2026.10.03",
  "packages": [
    {
      "id": "cozinha-01",
      "name": "Cozinha 01",
      "category": "Cozinha",
      "url": "https://github.com/USUARIO/REPOSITORIO/releases/latest/download/cozinha-01.zip",
      "sha256": "HASH_SHA256_EM_MAIUSCULAS",
      "bytes": 123456789
    }
  ]
}
```

A biblioteca local analisada tem cerca de 15,09 GB. Por isso, categorias maiores que 2 GB devem ser divididas em mais de um pacote quando forem publicadas no GitHub Releases.
