# BIM · Biblioteca de Famílias — Revit

Módulo nativo WPF na aba **Familia → Biblioteca de Famílias**, instalado ao lado do BIM Copiloto. O catálogo consulta as famílias do disco sem mover, renomear ou salvar os arquivos da biblioteca.

## Uso

1. Clique em **Biblioteca de Famílias** na aba Familia.
2. Use o botão **Ativador** na mesma aba para consultar se o sistema está ativado, ver quantos dias restam do teste e escolher **Ativar agora**.
2. Pesquise um nome, categoria ou etiqueta. A busca aceita vários termos e ignora acentos; também reconhece sinônimos comuns em inglês e espanhol.
3. Selecione categoria, subcategoria e ordenação. O filtro de versão usa metadados verificados pelo Revit.
4. Clique em um cartão para ver a prévia, localização, tamanho e etiquetas.
5. Marque a estrela para favoritar; use caixas de seleção para carregar até 50 famílias por lote. Um clique no cartão mostra os detalhes; dois cliques carregam imediatamente a família no Revit. Para renomear, selecione somente uma família e use **Renomear**, ou use **Renomear arquivo** nos detalhes. Para remover uma ou várias famílias da biblioteca, selecione-as e use **Excluir do HD**; o complemento pede confirmação e envia os `.rfa` para a Lixeira do Windows, permitindo restauração.
6. Clique em **Carregar no projeto** e confira o nome do projeto de destino na confirmação do Revit. Antes de enviar ao Revit, o módulo espera os arquivos selecionados terminarem de ser copiados/atualizados e confirma que estão estáveis. A opção **Ocultar após inserir** vem ativada e esconde a biblioteca quando o modo **Inserir Componente** começa; desmarque-a se quiser manter o painel visível. A primeira família carregada fica pronta para você clicar na posição desejada. As famílias já existentes são preservadas. O histórico de recentes registra cargas bem-sucedidas.
7. Em **⋯**, adicione pastas no D:, E: ou na rede, verifique duplicatas, consulte versões e exporte resultados em CSV.
8. Em **⋯ → Atualizar famílias .rfa para o Revit atual**, selecione uma pasta. A atualização começa automaticamente, sem confirmação, e processa uma família por vez no mesmo caminho, substituindo os arquivos antigos compatíveis. Se uma família der erro, ela é registrada e a próxima continua. O painel mostra em tempo real o percentual, atualizados, ignorados, erros e o arquivo atual; o relatório detalhado é salvo em `%LOCALAPPDATA%\BIMFamilias\atualizacoes-AAAAmmdd-HHmmss.jsonl`. A opção **Excluir duplicatas exatas na próxima atualização (.rfa)** fica desmarcada por padrão; quando marcada, compara por SHA-256 somente os arquivos da pasta escolhida, mantém uma cópia e exclui os demais conteúdos idênticos.
9. Use o botão **—** no cabeçalho para ocultar a biblioteca. Reabra-a pelo botão da aba Familia. Use o botão de bandeira em cada cartão, ou **Fixar no topo** nos detalhes, para manter as famílias mais usadas no início da lista. A fixação é pessoal e permanece após reindexar a biblioteca.

Na primeira abertura da biblioteca após cada instalação, informe o código de ativação. O código é válido somente durante a hora indicada; se estiver expirado ou incorreto, a janela informa essa condição. **Solicitar ativação** abre o WhatsApp cadastrado; **Teste por 7 dias** inicia uma única avaliação local. O gerador administrativo fica em `dist\ativador\BimFamilias.Ativador.exe`; por exemplo, para 02/10/2026 às 20:15, o código é `880-20-0210`. A ativação fica salva em `%LOCALAPPDATA%\BIMFamilias\ativacao.json` e é vinculada ao identificador criado pelo instalador.

O painel pode ser encaixado ou flutuar usando o mecanismo de painéis do Revit. Em largura reduzida, categorias passam para um seletor e detalhes abrem em uma janela.

## Dados reais e limites

- Fonte inicial: `E:\Projeto Em Andamento\#Familia\00 - Biblioteca Organizada`.
- Apenas arquivos **.rfa** são indexados. Projetos .rvt e templates não entram na pesquisa.
- Categorias e subcategorias refletem a organização atual das pastas. Etiquetas são pessoais e não alteram os arquivos.
- Prévias são extraídas das imagens PNG incorporadas, quando presentes e legíveis nos primeiros 4 MB. Arquivos sem essa prévia mostram **Prévia indisponível**. O módulo não renderiza um modelo 3D interativo.
- Versões desconhecidas mostram **A verificar**. Use **⋯ → Verificar versões desta página no Revit**; a versão também é verificada ao carregar. Arquivos mais novos que o Revit são recusados.
- A atualização automática desta tela trabalha com arquivos `.rfa`. Ela só converte famílias antigas para a versão do Revit em execução; arquivos já atuais, mais novos, somente leitura, corrompidos ou incompatíveis são preservados, registrados e não interrompem a fila.
- Se o `BasicFileInfo` não conseguir ler a versão de uma família, o complemento tenta abrir e salvar pelo próprio Revit. Se o Revit também rejeitar o formato, a família é preservada e aparece como ignorada com o motivo.
- A exclusão de duplicatas considera conteúdo binário idêntico por SHA-256, nunca apenas nomes iguais. O recurso mantém uma cópia determinística, exclui somente os `.rfa` da pasta selecionada e preserva arquivos somente leitura ou bloqueados.
- O ativador usa o padrão solicitado `880-HH-DDMM`; como o código é determinístico e não usa servidor, ele serve como controle simples de primeiro uso, não como proteção criptográfica forte contra cópia.
- Duplicatas são confirmadas por SHA-256, sob demanda. A operação é cancelável e não exclui arquivos. Nomes iguais, por si só, não comprovam duplicidade.
- Índice e preferências ficam em `%LOCALAPPDATA%\BIMFamilias\catalogo.json`, com cópia anterior `.bak`. Arquivos indisponíveis por desconexão de disco são informados.
- Registro de cargas: `%LOCALAPPDATA%\BIMFamilias\carregamentos.jsonl`.
- Sem dependência de IA, chave de API ou internet para pesquisar e carregar.
- O instalador detecta as versões do Revit e procura a build correspondente em `dist\revit-AAAA`. Esta entrega contém a build validada para **Revit 2027 / .NET 10**; versões antigas ou diferentes precisam de uma compilação própria contra suas respectivas `RevitAPI.dll` e `RevitAPIUI.dll`.
- O catálogo avulso em `dist\catalogo-final` permite pesquisa e prévias; carregar famílias requer o painel dentro do Revit.

## Instalação e código

O pacote contém fontes C#, XAML, compilados, testes e `install.ps1`. Para instalar com somente um arquivo, execute `dist\BimFamilias-Instalador-Unico.exe`; ele carrega internamente o instalador e os arquivos do plugin, extrai temporariamente e abre a janela de instalação. Como alternativa, a pasta `dist\instalador-exe` contém o instalador convencional com arquivos separados. A janela detecta as versões instaladas, permite marcar as versões com build compatível e informa as versões sem build. O instalador PowerShell também permanece disponível; para instalar uma versão específica, use `-RevitYear 2027`. Ele não substitui o BIM Copiloto.

Para desativar, feche o Revit e mova somente `BimFamilias.addin` para fora de `%APPDATA%\Autodesk\Revit\Addins\2027`. As bibliotecas e os outros complementos permanecem no lugar.

O carregamento segue a API de eventos externos do Revit e transações com reversão em caso de erro. Documentação: [painéis acopláveis da Autodesk](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Dockable_Dialog_Panes_html.html).

