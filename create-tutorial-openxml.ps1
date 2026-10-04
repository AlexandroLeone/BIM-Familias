$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$shots = Join-Path $root 'tutorial-screens'
$out = Join-Path $root 'Tutorial-BIM-Familias.pptx'
$EMU = 12700
$W = 960
$H = 540
$navy = '182B3A'
$teal = '1595A3'
$tealDark = '0E6470'
$ink = '1F2933'
$muted = '5B6872'
$light = 'E8EEF1'
$pale = 'F5F8FA'
$white = 'FFFFFF'
$gold = 'D49A28'
$red = 'B43C3C'

$imageMap = @{}
$nextImage = 1
$slideStates = New-Object System.Collections.ArrayList

function Xml-Escape([string]$value) {
    $source = [string]$value
    $source = $source.Replace('•','-').Replace('–','-').Replace('—','-').Replace('□','[ ]')
    $source = $source.Normalize([Text.NormalizationForm]::FormD)
    $clean = New-Object System.Text.StringBuilder
    foreach ($char in $source.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($char) -ne [Globalization.UnicodeCategory]::NonSpacingMark) { [void]$clean.Append($char) }
    }
    $escaped = [System.Security.SecurityElement]::Escape($clean.ToString())
    $builder = New-Object System.Text.StringBuilder
    foreach ($char in $escaped.ToCharArray()) {
        if ([int]$char -gt 127) { [void]$builder.Append('?') }
        else { [void]$builder.Append($char) }
    }
    return $builder.ToString()
}

function P([double]$v) { return [int]([Math]::Round($v * $EMU)) }

function New-State([string]$title, [string]$section) {
    return [pscustomobject]@{
        Title = $title
        Section = $section
        Shapes = New-Object System.Collections.ArrayList
        Images = New-Object System.Collections.ArrayList
        NextId = 2
    }
}

function Add-Rect($s, [double]$x, [double]$y, [double]$w, [double]$h, [string]$fill, [string]$line = '') {
    $id = $s.NextId; $s.NextId++
    $ln = if ($line) { '<a:ln w="12700"><a:solidFill><a:srgbClr val="' + $line + '"/></a:solidFill></a:ln>' } else { '<a:ln><a:noFill/></a:ln>' }
    $xml = '<p:sp><p:nvSpPr><p:cNvPr id="' + $id + '" name="Shape ' + $id + '"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="' + (P $x) + '" y="' + (P $y) + '"/><a:ext cx="' + (P $w) + '" cy="' + (P $h) + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="' + $fill + '"/></a:solidFill>' + $ln + '</p:spPr></p:sp>'
    [void]$s.Shapes.Add($xml)
}

function Add-Txt($s, [string]$text, [double]$x, [double]$y, [double]$w, [double]$h, [double]$size = 17, [string]$color = $ink, [bool]$bold = $false) {
    $id = $s.NextId; $s.NextId++
    $boldValue = if ($bold) { '1' } else { '0' }
    $paras = $text -split "`r?`n"
    $runs = ''
    foreach ($line in $paras) {
        if ([string]::IsNullOrEmpty($line)) {
            $runs += '<a:p><a:endParaRPr lang="pt-BR"/></a:p>'
        } else {
            $runs += '<a:p><a:r><a:rPr lang="pt-BR" sz="' + ([int]($size * 100)) + '" b="' + $boldValue + '"><a:solidFill><a:srgbClr val="' + $color + '"/></a:solidFill><a:latin typeface="Arial"/></a:rPr><a:t>' + (Xml-Escape $line) + '</a:t></a:r></a:p>'
        }
    }
    $xml = '<p:sp><p:nvSpPr><p:cNvPr id="' + $id + '" name="Text ' + $id + '"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="' + (P $x) + '" y="' + (P $y) + '"/><a:ext cx="' + (P $w) + '" cy="' + (P $h) + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr><p:txBody><a:bodyPr wrap="square"><a:spAutoFit/></a:bodyPr><a:lstStyle/>' + $runs + '</p:txBody></p:sp>'
    [void]$s.Shapes.Add($xml)
}

function Add-Img($s, [string]$path, [double]$x, [double]$y, [double]$w, [double]$h) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    if (-not $imageMap.ContainsKey($path)) {
        $imageMap[$path] = 'image' + $script:nextImage + '.png'
        $script:nextImage++
    }
    $rid = 'rId' + (2 + $s.Images.Count)
    [void]$s.Images.Add([pscustomobject]@{ Path = $path; Media = $imageMap[$path]; Rid = $rid })
    $id = $s.NextId; $s.NextId++
    $picId = 1026
    $xml = '<p:pic><p:nvPicPr><p:cNvPr id="' + $picId + '" name="Picture 2"/><p:cNvPicPr><a:picLocks noChangeAspect="1" noChangeArrowheads="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="' + $rid + '"/><a:srcRect/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr bwMode="auto"><a:xfrm><a:off x="' + (P $x) + '" y="' + (P $y) + '"/><a:ext cx="' + (P $w) + '" cy="' + (P $h) + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln w="9525"><a:noFill/><a:miter lim="800000"/><a:headEnd/><a:tailEnd/></a:ln><a:effectLst/></p:spPr></p:pic>'
    [void]$s.Shapes.Add($xml)
}

function Add-ImgFit($s, [string]$file, [double]$x, [double]$y, [double]$w, [double]$h) {
    $path = Join-Path $shots $file
    if (-not (Test-Path -LiteralPath $path)) { return }
    $img = [System.Drawing.Image]::FromFile($path)
    $iw = [double]$img.Width; $ih = [double]$img.Height
    $img.Dispose()
    $scale = [Math]::Min($w / $iw, $h / $ih)
    $nw = $iw * $scale; $nh = $ih * $scale
    $nx = $x + (($w - $nw) / 2); $ny = $y + (($h - $nh) / 2)
    Add-Rect $s ($x-5) ($y-5) ($w+10) ($h+10) $white $light
    Add-Img $s $path $nx $ny $nw $nh
}

function Header($s) {
    Add-Rect $s 0 0 $W 58 $navy
    Add-Txt $s $s.Title 24 13 700 32 25 $white $true
    Add-Txt $s $s.Section.ToUpperInvariant() 748 18 188 18 9 'B9E4E7' $true
    Add-Rect $s 24 515 912 1 $light
    Add-Txt $s 'BIM • Biblioteca de Famílias' 24 521 350 12 8 $muted $false
    Add-Txt $s ('Tutorial • ' + ($slideStates.Count + 1)) 850 521 86 12 8 $muted $false
}

function New-Base([string]$title, [string]$section) {
    $s = New-State $title $section
    Add-Rect $s 0 0 $W $H $white
    Header $s
    return $s
}

function Add-Body($s, [string]$text, [double]$x=34, [double]$y=94, [double]$w=385, [double]$h=380, [double]$size=16) {
    Add-Txt $s $text $x $y $w $h $size $ink $false
}

function Add-Card($s, [string]$title, [string]$text, [double]$x, [double]$y, [double]$w, [double]$h, [string]$accent=$teal) {
    Add-Rect $s $x $y $w $h $pale $light
    Add-Rect $s $x $y 7 $h $accent
    Add-Txt $s $title ($x+20) ($y+16) ($w-35) 24 16 $navy $true
    Add-Txt $s $text ($x+20) ($y+50) ($w-35) ($h-62) 13 $ink $false
}

function Add-Section([string]$number, [string]$title, [string]$subtitle) {
    $s = New-Base $title $number
    Add-Rect $s 65 110 8 290 $teal
    Add-Txt $s $number 105 112 180 55 34 '8EDBE0' $true
    Add-Txt $s $title 105 190 720 60 32 $navy $true
    Add-Txt $s $subtitle 107 285 700 80 20 $muted $false
    [void]$slideStates.Add($s)
}

function Add-ImageSlide([string]$title, [string]$section, [string]$file, [string]$text, [bool]$full=$false) {
    $s = New-Base $title $section
    if ($full) {
        Add-Txt $s $text 34 75 890 30 14 $muted $false
        Add-ImgFit $s $file 34 112 892 378
    } else {
        Add-Body $s $text
        Add-ImgFit $s $file 430 92 500 390
    }
    [void]$slideStates.Add($s)
}

# Capa
$s = New-State 'Famílias, ao seu alcance.' 'Biblioteca de Famílias'
Add-Rect $s 0 0 $W $H $navy
Add-Rect $s 0 0 $W 12 $teal
Add-Txt $s 'BIBLIOTECA DE FAMÍLIAS' 58 76 840 32 14 '8EDBE0' $true
Add-Txt $s 'Famílias, ao seu alcance.' 58 128 820 84 42 $white $true
Add-Txt $s 'Tutorial completo do plugin BIM para Revit' 62 238 700 30 21 'D7E5EA' $false
Add-Rect $s 62 315 660 2 $teal
Add-Txt $s 'Pesquisa • categorias • carregamento • inserção • atualização • organização' 62 340 800 30 15 'D7E5EA' $false
Add-Txt $s 'Demonstração baseada no Revit 2027' 62 445 500 22 11 '8EDBE0' $true
Add-Txt $s '01' 850 450 70 40 22 $white $true
[void]$slideStates.Add($s)

Add-Section '01' 'Comece pelo acesso' 'Instalação, projeto aberto e ativação do plugin.'
Add-ImageSlide 'O que você vai aprender' 'Introdução' '06-biblioteca-principal.png' "• Encontrar famílias por busca e categorias.`n• Escolher favoritos, recentes, duplicadas e itens a revisar.`n• Carregar e inserir a família na planta.`n• Atualizar RFA, acompanhar percentual e gerar relatório.`n• Organizar a biblioteca sem mexer em Projeto."
Add-ImageSlide 'Visão geral da biblioteca' 'Introdução' '06-biblioteca-principal.png' 'A tela combina categorias, busca, filtros, cartões, painel Em foco e ações em lote. O índice local informa quando a biblioteca está pronta.' $true
Add-ImageSlide 'Antes de começar' 'Preparação' '01-projeto-revit.png' "• Abra um projeto do Revit.`n• Confirme a pasta da biblioteca, por exemplo E:\Projeto Em Andamento\#Familia.`n• Não mova nem altere a pasta de projetos.`n• Faça backup antes de excluir ou atualizar em lote."
Add-ImageSlide 'Instalação em um único EXE' 'Preparação' '02-aba-familia.png' 'Feche o Revit, execute o instalador único, escolha a versão instalada e abra o Revit novamente. Se o EXE for bloqueado, use o pacote alternativo com mais arquivos.'
Add-ImageSlide 'Abrir o projeto no Revit' 'Primeiro acesso' '01-projeto-revit.png' 'Abra a vista em que deseja inserir. A biblioteca trabalha com um projeto aberto e deixa a planta pronta para receber o componente.'
Add-ImageSlide 'Localizar a aba Família' 'Primeiro acesso' '02-aba-familia.png' 'Clique em Família. Biblioteca de Famílias abre o painel principal; Ativador mostra o estado da licença, o teste e as opções de ativação.'
Add-ImageSlide 'Verificar o estado do sistema' 'Ativação' '03-status-nao-ativado.png' 'O status indica se o sistema está ativado. Quando não está, a tela oferece Ativar agora, Solicitar ativação e Teste por 7 dias.'
Add-ImageSlide 'Ativar ou iniciar o teste' 'Ativação' '04-ativacao.png' 'A tela mostra a hora local e o campo do código. Use Solicitar ativação, Teste por 7 dias, Ativar ou Sair conforme a situação.'
Add-ImageSlide 'Entender a validade do código' 'Ativação' '05-codigo-invalido.png' 'O padrão é 880-HH-DDMM: 880 é fixo, HH representa a hora e DDMM representa dia e mês. O código é aceito somente dentro da hora correspondente.'
$s = New-Base 'Período de teste e status' 'Ativação'
Add-Card $s 'Ativado' 'Biblioteca liberada para uso normal.' 55 125 260 145 $teal
Add-Card $s 'Teste ativo' 'Mostra quantos dias, horas e minutos ainda estão disponíveis.' 350 125 260 145 $gold
Add-Card $s 'Não ativado' 'Permite ativar agora, solicitar código ou iniciar o teste.' 645 125 260 145 $red
Add-Txt $s 'Ative antes de distribuir o plugin e documente o código fora do projeto.' 55 345 850 50 18 $muted $false
[void]$slideStates.Add($s)

Add-Section '02' 'Encontre qualquer família' 'Pesquise, filtre e compare antes de carregar.'
Add-ImageSlide 'Conhecer a tela principal' 'Biblioteca' '06-biblioteca-principal.png' 'A coluna esquerda traz categorias; o centro reúne busca e filtros; os cartões exibem as famílias; o painel Em foco detalha o item selecionado.'
Add-ImageSlide 'Pesquisar por nome ou palavra-chave' 'Biblioteca' '07-pesquisa-sofa.png' 'Clique no campo ou use Ctrl+F. Digite parte do nome ou uma palavra-chave e combine a pesquisa com os filtros.'
Add-ImageSlide 'Filtrar por categoria' 'Biblioteca' '08-categoria-cozinha.png' 'Use a coluna esquerda para entrar em Cozinha, Sala, Banheiro, Portas, Janelas e outras famílias recorrentes.'
Add-ImageSlide 'Refinar com subcategorias' 'Biblioteca' '09-subcategorias.png' 'Abra Todas as subcategorias para separar armários, bancadas, eletrodomésticos, mesas, cadeiras e outros grupos.'
Add-ImageSlide 'Filtrar por versão do Revit' 'Biblioteca' '10-filtro-versoes.png' 'Use Todas as versões para listar tudo ou A verificar para concentrar arquivos que ainda precisam de conferência.'
$s = New-Base 'Ordenar e alternar a visualização' 'Biblioteca'
Add-Card $s 'Nome A–Z' 'Cria uma ordem previsível e localiza rapidamente pelo nome.' 45 110 270 145 $teal
Add-Card $s 'Grade' 'Ajuda a comparar visualmente as prévias.' 345 110 270 145 $gold
Add-Card $s 'Lista' 'Prioriza caminho, tamanho, versão e informações textuais.' 645 110 270 145 $tealDark
Add-Txt $s 'Use grade para escolher visualmente e lista para auditar os arquivos.' 45 340 850 40 19 $muted $false
[void]$slideStates.Add($s)
Add-ImageSlide 'Marcar e consultar favoritos' 'Biblioteca' '11-favoritos.png' 'Clique na estrela do cartão e depois em Favoritas. Mantenha somente os modelos aprovados e mais usados.'
Add-ImageSlide 'Reabrir famílias recentes' 'Biblioteca' '12-recentes.png' 'Recentes reúne famílias consultadas ou carregadas há pouco tempo e reduz a repetição de buscas.'
Add-ImageSlide 'Localizar duplicadas' 'Biblioteca' '13-duplicadas.png' 'A aba Duplicadas mostra o resultado da conferência por conteúdo. Se necessário, use Verificar duplicatas por conteúdo no menu ….'
Add-ImageSlide 'Separar o que precisa de revisão' 'Biblioteca' '14-a-revisar.png' 'A revisar reúne itens sem confirmação de versão, sem prévia ou com metadados incompletos.'

Add-Section '03' 'Trabalhe pelo cartão' 'Detalhes, seleção múltipla, favoritos e organização.'
Add-ImageSlide 'Abrir os detalhes de uma família' 'Cartões' '15-card-selecionado.png' 'Clique uma vez no cartão. Em foco mostra nome, categoria, tamanho, versão, modificação e ações disponíveis.'
Add-ImageSlide 'Entender os controles do cartão' 'Cartões' '22-dica-cartao.png' 'Clique simples mostra detalhes; duplo clique carrega no Revit; a caixa de seleção serve para lote; a estrela marca favorito.' $true
Add-ImageSlide 'Selecionar várias famílias' 'Operações em lote' '16-selecao-multipla.png' 'Marque duas ou mais caixas. A barra inferior oferece Carregar seleção, Renomear e Excluir do HD, sempre mostrando a quantidade.'
Add-ImageSlide 'Favoritar uma família diretamente' 'Cartões' '17-favoritar-familia.png' 'A estrela preenchida envia o item para Favoritas e ajuda a montar uma lista de trabalho recorrente.'
Add-ImageSlide 'Renomear arquivo com segurança' 'Organização' '15-card-selecionado.png' 'Use Renomear arquivo no painel Em foco. Padronize o nome e preserve a extensão .rfa; depois atualize o índice.'
Add-ImageSlide 'Fixar os mais usados no topo' 'Organização' '15-card-selecionado.png' 'Fixar no topo prioriza uma família nos resultados. A preferência é de organização e não altera o conteúdo do RFA.'
Add-ImageSlide 'Abrir a pasta física da família' 'Organização' '15-card-selecionado.png' 'Abrir pasta ajuda a conferir caminho, permissões e duplicidades. Evite mover arquivos durante a atualização do índice.'

Add-Section '04' 'Carregue e insira' 'Do cartão à posição exata dentro da planta.'
Add-ImageSlide 'Carregar a família no projeto' 'Carregamento' '21-confirmacao-carregamento.png' 'Selecione a família e clique Carregar no projeto. A confirmação informa a quantidade e preserva famílias já existentes.'
Add-ImageSlide 'Duplo clique: carregar e inserir' 'Carregamento' '20-inserir-componente.png' 'O duplo clique carrega, oculta o painel quando configurado e coloca o Revit em Modificar | Colocar Componente. Clique na planta para posicionar.'
Add-ImageSlide 'Ocultar a biblioteca após inserir' 'Carregamento' '19-biblioteca-minimizada.png' 'Marque Ocultar após inserir no cabeçalho. Para reabrir, clique em Biblioteca de Famílias na aba Família.'

Add-Section '05' 'Mantenha a biblioteca saudável' 'Atualização de RFA, progresso, relatório e duplicados.'
Add-ImageSlide 'Abrir o menu de manutenção' 'Manutenção' '18-menu-atualizar.png' 'O menu … oferece pastas cadastradas, duplicatas, versões, exclusão de cópias exatas, atualização de RFA, CSV e diagnóstico.' $true
$s = New-Base 'Atualizar famílias sem interromper' 'Manutenção'
Add-Card $s 'Identificar' 'Mostra quais arquivos têm versão diferente.' 45 105 270 145 $teal
Add-Card $s 'Compatibilizar' 'Atualiza somente RFA compatível com o Revit atual.' 345 105 270 145 $gold
Add-Card $s 'Continuar' 'Ignora o arquivo com problema e segue para o próximo.' 645 105 270 145 $tealDark
Add-Txt $s 'Faça backup e deixe a rotina trabalhar sem exigir confirmação a cada arquivo.' 45 335 850 35 18 $muted $false
[void]$slideStates.Add($s)
$s = New-Base 'Acompanhar o percentual em tempo real' 'Manutenção'
Add-Txt $s 'Atualização em andamento' 75 120 500 30 22 $navy $true
Add-Rect $s 75 185 790 30 $light
Add-Rect $s 75 185 505 30 $teal
Add-Txt $s '64%' 410 188 120 24 16 $white $true
Add-Txt $s 'Arquivos processados: 128 de 200' 75 245 400 24 17 $ink $false
Add-Txt $s 'Sucessos: 121    Ignorados: 5    Erros: 2' 75 283 550 24 17 $muted $false
Add-Txt $s 'O percentual deve continuar visível para você acompanhar o avanço sem confirmar cada item.' 75 355 760 55 18 $muted $false
[void]$slideStates.Add($s)
$s = New-Base 'Ler o relatório da atualização' 'Manutenção'
Add-Card $s 'Atualizados' 'Arquivos substituídos pela versão compatível.' 50 115 270 155 $teal
Add-Card $s 'Ignorados' 'Incompatíveis, ausentes, bloqueados ou sem atualização.' 345 115 270 155 $gold
Add-Card $s 'Erros' 'Arquivo, etapa e mensagem para correção posterior.' 640 115 270 155 $red
Add-Txt $s 'Revise somente as exceções; não repita manualmente os arquivos concluídos.' 50 350 850 45 18 $muted $false
[void]$slideStates.Add($s)
Add-ImageSlide 'Excluir duplicados com critério' 'Manutenção' '18-menu-atualizar.png' 'Use a comparação por conteúdo. Excluir duplicatas exatas deve remover somente cópias iguais; confirme a pasta e mantenha backup.'
Add-ImageSlide 'Excluir arquivos selecionados do HD' 'Manutenção' '16-selecao-multipla.png' 'Selecione os cartões e clique Excluir do HD somente depois de conferir nomes, caminhos e duplicidade. Nunca use para arquivos da pasta Projeto.'

Add-Section '06' 'Finalize com segurança' 'Organização recomendada, diagnóstico e checklist.'
$s = New-Base 'Fluxo recomendado de organização' 'Boas práticas'
Add-Txt $s '1' 70 120 48 48 30 $teal $true; Add-Txt $s 'Atualize o índice' 130 125 280 28 19 $navy $true
Add-Txt $s '2' 70 205 48 48 30 $teal $true; Add-Txt $s 'Corrija categorias e subcategorias' 130 210 380 28 19 $navy $true
Add-Txt $s '3' 70 290 48 48 30 $teal $true; Add-Txt $s 'Revise versões e duplicidades' 130 295 380 28 19 $navy $true
Add-Txt $s '4' 70 375 48 48 30 $teal $true; Add-Txt $s 'Renomeie e fixe os mais usados' 130 380 380 28 19 $navy $true
Add-Rect $s 585 120 300 285 'E9F7F7' $teal
Add-Txt $s 'Resultado esperado' 615 160 240 28 20 $tealDark $true
Add-Txt $s 'Uma família de cozinha fica na pasta de cozinha; sala na pasta de sala; e assim sucessivamente, sem cópias espalhadas.' 615 215 235 125 17 $ink $false
[void]$slideStates.Add($s)
$s = New-Base 'Quando algo não funciona' 'Solução de problemas'
Add-Card $s 'Família não carrega' 'Confira a versão, permissões e se o projeto está aberto.' 45 105 270 140 $red
Add-Card $s 'Atualização falha' 'Leia o relatório, ignore o item problemático e continue com os demais.' 345 105 270 140 $gold
Add-Card $s 'Cartão sem prévia' 'Use detalhes ou Abrir pasta; a ausência de imagem não impede a auditoria.' 645 105 270 140 $teal
Add-Txt $s 'Use Último diagnóstico no menu … para revisar a última execução.' 45 335 850 35 18 $muted $false
[void]$slideStates.Add($s)
$s = New-Base 'Checklist rápido do usuário' 'Encerramento'
Add-Txt $s '□ Revit aberto com o projeto correto`n□ Aba Família localizada`n□ Ativação ou teste conferido`n□ Busca e categoria selecionadas`n□ Versão do arquivo verificada`n□ Família carregada e inserida na planta`n□ Ocultar após inserir ajustado`n□ Backup feito antes de excluir ou atualizar`n□ Relatório lido após a manutenção' 70 105 560 320 20 $ink $false
Add-Rect $s 680 135 220 220 'E9F7F7' $teal
Add-Txt $s 'Pronto' 730 180 125 38 30 $tealDark $true
Add-Txt $s 'Biblioteca centralizada, pesquisável e pronta para uso no Revit.' 710 245 165 75 16 $ink $false
[void]$slideStates.Add($s)
$s = New-Base 'Resumo das funções' 'Encerramento'
Add-Txt $s 'Acesso' 50 105 180 24 18 $tealDark $true; Add-Txt $s 'Ribbon Família • Biblioteca de Famílias • Ativador' 250 105 600 24 16 $ink $false
Add-Txt $s 'Pesquisa' 50 155 180 24 18 $tealDark $true; Add-Txt $s 'Nome • categoria • subcategoria • versão • ordenação' 250 155 600 24 16 $ink $false
Add-Txt $s 'Ações' 50 205 180 24 18 $tealDark $true; Add-Txt $s 'Detalhes • favorito • recente • carregar • inserir • renomear • fixar' 250 205 650 24 16 $ink $false
Add-Txt $s 'Manutenção' 50 255 180 24 18 $tealDark $true; Add-Txt $s 'Atualizar RFA • progresso • relatório • duplicados • exclusão • diagnóstico' 250 255 650 24 16 $ink $false
Add-Txt $s 'Princípio' 50 330 180 24 18 $tealDark $true; Add-Txt $s 'Organizar a biblioteca sem mexer na pasta de projetos.' 250 330 600 24 18 $navy $true
[void]$slideStates.Add($s)
$s = New-State 'Fim do tutorial' 'Biblioteca de Famílias'
Add-Rect $s 0 0 $W $H $navy
Add-Txt $s 'FIM DO TUTORIAL' 70 120 820 34 15 '8EDBE0' $true
Add-Txt $s 'Famílias, ao seu alcance.' 70 178 820 66 38 $white $true
Add-Txt $s 'Use a biblioteca como ponto único de busca, conferência e inserção no Revit.' 72 285 760 55 21 'D7E5EA' $false
Add-Rect $s 72 380 610 2 $teal
Add-Txt $s 'Tutorial gerado com capturas reais do plugin em execução.' 72 410 750 25 14 '8EDBE0' $false
Add-Txt $s ([string]($slideStates.Count + 1)) 850 450 70 40 22 $white $true
[void]$slideStates.Add($s)

function Add-ZipText($zip, [string]$name, [string]$text) {
    $entry = $zip.CreateEntry($name)
    $stream = $entry.Open()
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Dispose()
}

function Add-ZipBytes($zip, [string]$name, [byte[]]$bytes) {
    $entry = $zip.CreateEntry($name)
    $stream = $entry.Open()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Dispose()
}

# Use a PowerPoint-generated base package so all master, layout, theme and
# document-property parts remain fully compatible with Office 2007.
$base = Join-Path $root 'tutorial-base.pptx'
if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Force }
$pp = New-Object -ComObject PowerPoint.Application
$pp.Visible = $true
$pres = $pp.Presentations.Add()
for ($i = 1; $i -le $slideStates.Count; $i++) { [void]$pres.Slides.Add($i, 12) }
$pres.SaveAs($base, 24)
$pres.Close()
$pp.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($pres) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($pp) | Out-Null

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
$baseZip = [System.IO.Compression.ZipFile]::OpenRead($base)
$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)

foreach ($entry in $baseZip.Entries) {
    if ($entry.FullName -eq '[Content_Types].xml') {
        $sr = New-Object IO.StreamReader($entry.Open())
        $contentTypes = $sr.ReadToEnd(); $sr.Dispose()
        if ($contentTypes -notmatch 'Extension="png"') { $contentTypes = $contentTypes.Replace('</Types>', '<Default Extension="png" ContentType="image/png"/></Types>') }
        Add-ZipText $zip $entry.FullName $contentTypes
        continue
    }
    $slideMatch = [regex]::Match($entry.FullName, '^ppt/slides/slide([0-9]+)\.xml$')
    if ($slideMatch.Success) {
        $index = [int]$slideMatch.Groups[1].Value - 1
        $state = $slideStates[$index]
        $slideXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>' + ([string]::Join('', [string[]]$state.Shapes)) + '</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>'
        Add-ZipText $zip $entry.FullName $slideXml
        continue
    }
    $relsMatch = [regex]::Match($entry.FullName, '^ppt/slides/_rels/slide([0-9]+)\.xml\.rels$')
    if ($relsMatch.Success) {
        $index = [int]$relsMatch.Groups[1].Value - 1
        $state = $slideStates[$index]
        $sr = New-Object IO.StreamReader($entry.Open())
        $rels = $sr.ReadToEnd(); $sr.Dispose()
        foreach ($image in $state.Images) { $rels = $rels.Replace('</Relationships>', '<Relationship Id="' + $image.Rid + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/' + $image.Media + '"/></Relationships>') }
        Add-ZipText $zip $entry.FullName $rels
        continue
    }
    $target = $zip.CreateEntry($entry.FullName)
    $input = $entry.Open(); $output = $target.Open(); $input.CopyTo($output); $output.Dispose(); $input.Dispose()
}

foreach ($entry in $imageMap.GetEnumerator()) { Add-ZipBytes $zip ('ppt/media/' + $entry.Value) ([System.IO.File]::ReadAllBytes($entry.Key)) }
$baseZip.Dispose()
$zip.Dispose()
Write-Output ('Criado: ' + $out)
Write-Output ('Slides: ' + $slideStates.Count)
Write-Output ('Imagens incorporadas: ' + $imageMap.Count)
exit 0

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)

$rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/></Relationships>'
Add-ZipText $zip '_rels/.rels' $rootRels

$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/><Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/><Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/><Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/><Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/><Override PartName="/ppt/presProps.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presProps+xml"/><Override PartName="/ppt/viewProps.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml"/><Override PartName="/ppt/tableStyles.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml"/>'
for ($i=1; $i -le $slideStates.Count; $i++) { $contentTypes += '<Override PartName="/ppt/slides/slide' + $i + '.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>' }
$contentTypes += '</Types>'
Add-ZipText $zip '[Content_Types].xml' $contentTypes

$presRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="slideMasters/slideMaster1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="theme/theme1.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps" Target="presProps.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps" Target="viewProps.xml"/><Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles" Target="tableStyles.xml"/>'
for ($i=1; $i -le $slideStates.Count; $i++) { $presRels += '<Relationship Id="rId' + (5+$i) + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide' + $i + '.xml"/>' }
$presRels += '</Relationships>'
Add-ZipText $zip 'ppt/_rels/presentation.xml.rels' $presRels

$presentation = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst><p:sldIdLst>'
for ($i=1; $i -le $slideStates.Count; $i++) { $presentation += '<p:sldId id="' + (255+$i) + '" r:id="rId' + (5+$i) + '"/>' }
$presentation += '</p:sldIdLst><p:sldSz cx="12192000" cy="6858000" type="screen16x9"/><p:notesSz cx="6858000" cy="9144000"/><p:defaultTextStyle><a:defPPr/></p:defaultTextStyle></p:presentation>'
Add-ZipText $zip 'ppt/presentation.xml' $presentation

$theme = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="BIM"><a:themeElements><a:clrScheme name="BIM"><a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1><a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="182B3A"/></a:dk2><a:lt2><a:srgbClr val="F5F8FA"/></a:lt2><a:accent1><a:srgbClr val="1595A3"/></a:accent1><a:accent2><a:srgbClr val="D49A28"/></a:accent2><a:accent3><a:srgbClr val="B43C3C"/></a:accent3><a:accent4><a:srgbClr val="0E6470"/></a:accent4><a:accent5><a:srgbClr val="5B6872"/></a:accent5><a:accent6><a:srgbClr val="E8EEF1"/></a:accent6><a:hlink><a:srgbClr val="1595A3"/></a:hlink><a:folHlink><a:srgbClr val="0E6470"/></a:folHlink></a:clrScheme><a:fontScheme name="BIM"><a:majorFont><a:latin typeface="Arial"/></a:majorFont><a:minorFont><a:latin typeface="Arial"/></a:minorFont></a:fontScheme><a:fmtScheme name="BIM"><a:fillStyleLst/><a:lnStyleLst/><a:effectStyleLst/><a:bgFillStyleLst/></a:fmtScheme></a:themeElements></a:theme>'
Add-ZipText $zip 'ppt/theme/theme1.xml' $theme

$master = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld name="BIM"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/><p:sldLayoutIdLst><p:sldLayoutId id="1" r:id="rId1"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>'
Add-ZipText $zip 'ppt/slideMasters/slideMaster1.xml' $master
Add-ZipText $zip 'ppt/slideMasters/_rels/slideMaster1.xml.rels' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/></Relationships>'
$layout = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>'
Add-ZipText $zip 'ppt/slideLayouts/slideLayout1.xml' $layout
Add-ZipText $zip 'ppt/slideLayouts/_rels/slideLayout1.xml.rels' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>'
Add-ZipText $zip 'ppt/presProps.xml' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:presentationPr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"/>'
Add-ZipText $zip 'ppt/viewProps.xml' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:viewPr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" lastView="sldView"><p:normalViewPr/><p:slideViewPr/><p:notesViewPr/><p:gridSpacing cx="72008" cy="72008"/></p:viewPr>'
Add-ZipText $zip 'ppt/tableStyles.xml' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><a:tblStyleLst xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" def=""/>'
Add-ZipText $zip 'docProps/core.xml' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>Tutorial BIM - Biblioteca de Famílias</dc:title><dc:subject>Guia de utilização do plugin para Revit</dc:subject><dc:creator>OpenAI</dc:creator><cp:lastModifiedBy>OpenAI</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">2026-10-02T00:00:00Z</dcterms:created></cp:coreProperties>'
Add-ZipText $zip 'docProps/app.xml' '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><Application>Microsoft Office PowerPoint</Application><PresentationFormat>Widescreen</PresentationFormat><Slides>48</Slides><Notes>0</Notes><HiddenSlides>0</HiddenSlides><MMClips>0</MMClips><ScaleCrop>false</ScaleCrop><HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>Slides</vt:lpstr></vt:variant><vt:variant><vt:i4>48</vt:i4></vt:variant></vt:vector></HeadingPairs><TitlesOfParts><vt:vector size="48" baseType="lpstr"/></TitlesOfParts></Properties>'

for ($i=0; $i -lt $slideStates.Count; $i++) {
    $state = $slideStates[$i]
    $slideXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld name="' + (Xml-Escape $state.Title) + '"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>' + ([string]::Join('', [string[]]$state.Shapes)) + '</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>'
    Add-ZipText $zip ('ppt/slides/slide' + ($i+1) + '.xml') $slideXml
    $rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/>'
    foreach ($image in $state.Images) { $rels += '<Relationship Id="' + $image.Rid + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/' + $image.Media + '"/>' }
    $rels += '</Relationships>'
    Add-ZipText $zip ('ppt/slides/_rels/slide' + ($i+1) + '.xml.rels') $rels
}

foreach ($entry in $imageMap.GetEnumerator()) {
    Add-ZipBytes $zip ('ppt/media/' + $entry.Value) ([System.IO.File]::ReadAllBytes($entry.Key))
}

$zip.Dispose()
Write-Output ('Criado: ' + $out)
Write-Output ('Slides: ' + $slideStates.Count)
Write-Output ('Imagens incorporadas: ' + $imageMap.Count)
