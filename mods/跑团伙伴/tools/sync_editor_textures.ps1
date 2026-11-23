param(
    [string]$Managed = 'E:/SteamLibrary/steamapps/common/Depersonalization/Depersonalization-Release_Data/Managed',
    [string]$ProjectPath
)
$ErrorActionPreference='Stop'
$project = if ($ProjectPath) {
    [IO.Path]::GetFullPath($ProjectPath)
} else {
    Join-Path (Split-Path -Parent $PSScriptRoot) 'Project_Depersonal'
}
$texture=Join-Path $project 'Assets/Resources/Texture'
$versionFile=Join-Path $project 'Project.rtmeta'
$pngs=@(foreach($folder in 'Charator','Effect','MagicCard','Item','Buff','HeadIcon'){
    $path=Join-Path $texture $folder
    if(Test-Path -LiteralPath $path){
        Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.png' | Sort-Object FullName
    }
})
if(!$pngs.Count){return}

if(!('PartnerEditorAssemblies' -as [type])){
    Add-Type -TypeDefinition @'
using System.IO;
using System.Runtime.Loader;

public static class PartnerEditorAssemblies
{
    public static AssemblyLoadContext Open(string directory)
    {
        var context = new AssemblyLoadContext("PartnerEditorAssets");
        context.Resolving += (loader, name) =>
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? loader.LoadFromAssemblyPath(path) : null;
        };
        return context;
    }
}
'@
}
$Managed=[IO.Path]::GetFullPath($Managed)
$context=[PartnerEditorAssemblies]::Open($Managed)
$protobuf=$context.LoadFromAssemblyPath((Join-Path $Managed 'protobuf-net.dll'))
$editor=$context.LoadFromAssemblyPath((Join-Path $Managed 'Battlehub.RTEditor.dll'))
$serializer=$protobuf.GetType('ProtoBuf.Meta.RuntimeTypeModel',$true)::Create()
$projectType=$editor.GetType('Battlehub.RTSL.Interface.ProjectInfo',$true)
$itemBase=$editor.GetType('Battlehub.RTSL.Interface.ProjectItem',$true)
$itemType=$editor.GetType('Battlehub.RTSL.Interface.AssetItem`1',$true).MakeGenericType([long])
$viewType=$editor.GetType('Battlehub.RTSL.Interface.Preview`1',$true).MakeGenericType([long])
# Battlehub serializes PNG asset metadata as the long-ID subtype of ProjectItem.
[void]$serializer.Add($itemBase,$true).AddSubType(1028,$itemType)
$pngType=[guid]'4be06fec-1084-4e90-a179-1bf0e8c0e970'

function Read-EditorRecord([string]$Path,[type]$Type){
    $stream=[IO.File]::OpenRead($Path)
    try{$serializer.Deserialize($stream,$null,$Type)}finally{$stream.Dispose()}
}

function Write-EditorRecord([string]$Path,$Record){
    $stream=[IO.MemoryStream]::new()
    $temp=Join-Path (Split-Path -Parent $project) ('temp/editor-texture-'+[guid]::NewGuid()+'.tmp')
    try {
        $serializer.Serialize($stream,$Record)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($temp))
        [IO.File]::WriteAllBytes($temp,$stream.ToArray())
        [IO.File]::Move($temp,$Path,$true)
    } finally {
        $stream.Dispose()
        if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}
    }
}

$projectInfo=Read-EditorRecord $versionFile $projectType
$idGenerator=$editor.GetType('Battlehub.RTSL.ProjectAsyncImpl',$true)
$generator=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($idGenerator)
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$takeId=$idGenerator.GetMethod('GetOrdinalAndId',$flags)
$makeId=$idGenerator.GetMethod('ToDynamicResourceID',$flags)
$originalCounter=$projectInfo.AssetIdentifier
$records=@(foreach($png in $pngs){
    $meta=$png.FullName+'.rtmeta'
    if([IO.File]::Exists($meta)){
        $item=Read-EditorRecord $meta $itemBase
        if($item.GetType() -ne $itemType -or $item.TypeGuid -ne $pngType -or $item.ID -le 0){
            throw "编辑器 PNG 元数据无效：$meta"
        }
        continue
    }
    $next=[object[]]@($projectInfo.AssetIdentifier,0,0)
    if(!$takeId.Invoke($generator,$next)){throw '编辑器素材 ID 已用尽'}
    $projectInfo.AssetIdentifier=$next[0]
    $item=[Activator]::CreateInstance($itemType)
    $item.ID=$makeId.Invoke($generator,[object[]]@($next[1],$next[2]))
    $item.TypeGuid=$pngType
    $item.CustomDataOffset=-1
    [pscustomobject]@{File=$png;Item=$item}
})

# Reserve IDs before writing individual sidecars so an interrupted sync is safe to retry.
if($projectInfo.AssetIdentifier -ne $originalCounter){
    Write-EditorRecord $versionFile $projectInfo
}
foreach($record in $records){
    Write-EditorRecord ($record.File.FullName+'.rtmeta') $record.Item
}
foreach($png in $pngs){
    $viewPath=$png.FullName+'.rtview'
    $bytes=[IO.File]::ReadAllBytes($png.FullName)
    $item=Read-EditorRecord ($png.FullName+'.rtmeta') $itemBase
    if([IO.File]::Exists($viewPath)){
        $view=Read-EditorRecord $viewPath $viewType
        if($view.ID -eq $item.ID -and [Linq.Enumerable]::SequenceEqual[byte]($view.PreviewData,$bytes)){continue}
    }
    $view=[Activator]::CreateInstance($viewType)
    $view.ID=$item.ID
    $view.PreviewData=$bytes
    Write-EditorRecord $viewPath $view
}
Write-Output "编辑器动画资源已核对 $($pngs.Count) 帧，新增 $($records.Count) 项注册"
