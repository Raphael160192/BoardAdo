# Entre 4 Paredes — Azure DevOps MCP Server

Servidor MCP (Streamable HTTP) em .NET que expõe ao Claude ferramentas para criar
e consultar work items no Azure DevOps, com a hierarquia montada automaticamente.
Pensado para rodar no Render (free) e ser conectado ao Claude como *custom
connector*, permitindo gerenciar backlog por linguagem natural — inclusive do celular.

## Ferramentas expostas

### Criação

| Tool                | Cria       | parentId                              |
| ------------------- | ---------- | ------------------------------------- |
| `create_epic`       | Epic       | —                                     |
| `create_feature`    | Feature    | Opcional (id do épico)                |
| `create_user_story` | User Story | Obrigatório (id da feature ou épico)  |
| `create_task`       | Task       | Obrigatório (id da história)          |
| `create_bug`        | Bug        | Opcional (id da história ou feature)  |
| `create_issue`      | Issue      | Opcional (normalmente sem pai)        |

Cada tool devolve o `id` criado, então o Claude encadeia sozinho: cria o épico, usa
o id retornado como pai das features, e assim por diante até as tasks. A hierarquia
do process **Agile** é `Epic > Feature > User Story > Task`.

### Consulta

| Tool              | Devolve                                                              |
| ----------------- | -------------------------------------------------------------------- |
| `get_work_item`   | Um item pelo id: estado, responsável, descrição, id do pai e dos filhos. |
| `list_work_items` | Lista resumida, filtrável por `type`, `state`, `titleContains` e `parentId`. |

`list_work_items` ordena do alterado mais recentemente para o mais antigo e monta o
WIQL internamente — o Claude passa filtros estruturados, nunca query crua. Combinado
com `get_work_item`, dá para navegar a árvore do backlog item a item.

## Variáveis de ambiente

| Variável      | Descrição                                                            |
| ------------- | ------------------------------------------------------------------- |
| `ADO_ORG`     | Nome da organização no Azure DevOps (ex.: `minhaorg`).              |
| `ADO_PROJECT` | Nome do project (ex.: `Entre 4 Paredes`).                           |
| `ADO_PAT`     | Personal Access Token com escopo **Work Items → Read & Write**.     |
| `MCP_API_KEY` | Segredo forte enviado pelo Claude (query `?key=`) ou por header `Authorization`. |
| `PORT`        | Injetada automaticamente pelo Render. Não precisa configurar.       |

Gere o `MCP_API_KEY` como um segredo aleatório longo, por exemplo:

```bash
openssl rand -hex 32
```

> O project precisa usar o process **Agile** (ou Scrum), que é quem define os tipos
> `Epic`, `Feature`, `User Story`, `Task`, `Bug` e `Issue` que as tools criam. O process
> **Basic** não tem `User Story` nem `Feature`, e não dá para trocar o process depois
> de criar o project — escolha em **Advanced** na tela de criação.

## Gerar o PAT no Azure DevOps

1. Azure DevOps → canto superior direito → **User settings** → **Personal access tokens**.
2. **New Token**. Dê um nome, escolha a organização e uma expiração.
3. Em **Scopes**, marque **Work Items → Read & Write** (só isso; princípio do menor privilégio).
4. Copie o token — ele só aparece uma vez. Esse valor vai em `ADO_PAT` no Render.

## Deploy no Render

1. Suba este projeto num repositório Git (GitHub/GitLab).
2. No Render: **New** → **Web Service** → conecte o repositório.
3. Em **Runtime/Environment**, escolha **Docker** (o `Dockerfile` na raiz é detectado).
4. Selecione o plano **Free**.
5. Em **Environment Variables**, adicione `ADO_ORG`, `ADO_PROJECT`, `ADO_PAT` e `MCP_API_KEY`.
6. **Create Web Service**. Ao terminar, anote a URL pública (ex.: `https://entre-quiz-ado-mcp.onrender.com`).
7. Teste o health check abrindo `https://SUA-URL.onrender.com/health` → deve responder `{"status":"healthy"}`.

> No plano free o serviço hiberna após ~15 min sem tráfego. A primeira chamada
> depois disso leva 30–60s (cold start). Para gerar backlog sob demanda isso é
> irrelevante.

## Conectar no Claude (custom connector)

O connector é adicionado **uma vez pelo claude.ai no navegador**; depois sincroniza
para o app do celular.

1. Em claude.ai: **Settings** → **Connectors** → **Add custom connector**.
2. **Name**: `Azure DevOps – Entre 4 Paredes`.
3. **URL**: a raiz do serviço no Render, com a chave na query string:
   `https://SUA-URL.onrender.com/?key=<o mesmo valor de MCP_API_KEY>`
   (a UI atual do custom connector só tem campos de Nome, URL e OAuth opcional —
   não há mais campo de headers, por isso a chave vai na própria URL).
4. Deixe **OAuth Client ID/Secret** em branco.
5. Salve. O Claude fará o handshake e listará as três tools.

Feito isso, do celular você abre o app do Claude e escreve algo como:

> "Transforma essa ideia num épico com histórias e tasks em ordem de execução:
> [cola a ideia crua]. Antes de criar no Azure DevOps, me mostra a árvore proposta
> pra eu aprovar."

O Claude estrutura, você aprova, e ele cria tudo com a hierarquia certa.

## Rodar localmente (opcional, para testar antes do deploy)

```bash
export ADO_ORG=minhaorg
export ADO_PROJECT="Entre 4 Paredes"
export ADO_PAT=xxxxxxxx
export MCP_API_KEY=segredo-de-teste
export PORT=8080
dotnet run
```

Para inspecionar as tools sem o Claude, use o **MCP Inspector** apontando para
`http://localhost:8080` com o header `Authorization: Bearer segredo-de-teste`.

## Notas de segurança

- O `ADO_PAT` nunca sai do servidor — o Claude só conversa com este endpoint, nunca vê o token.
- O gate por `MCP_API_KEY` impede que qualquer um na internet use seu endpoint. Trate essa chave como senha.
- Como a chave vai na URL do connector (`?key=`), ela pode aparecer em logs de acesso do Render. Se isso for uma preocupação, gire (rotacione) a chave periodicamente.
- Escopo mínimo no PAT (só Work Items R/W) limita o estrago caso algo vaze.
- As tools só criam e leem work items — nenhuma edita ou apaga o que já existe.
