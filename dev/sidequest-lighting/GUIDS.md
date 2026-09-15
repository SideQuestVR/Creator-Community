# Frozen GUIDs

Every tool package embeds a byte-identical copy of Core at the same path. A
`.unitypackage` import is keyed on GUID **and** path, so when two of these packages
carry the same file with the same GUID at the same path, the second import overwrites
the first with identical content instead of creating a duplicate type.

That is the whole mechanism, and it fails silently if a GUID drifts: the user gets
`CS0101` duplicate definitions and no obvious cause. So these values are frozen here
and `Build-Packages.ps1` refuses to build when a `.meta` no longer matches.

Generated from the `.meta` files Unity wrote. Do not edit by hand. If a file is added,
let Unity import it and regenerate this list; if a GUID here ever needs to change, the
packages that shipped the old one have to be republished together.

| Path | GUID |
| --- | --- |
| `Assets/SideQuest/LightingTools/Bake` | `620fc92c5d5c5264da4e345231d2f3ad` |
| `Assets/SideQuest/LightingTools/Core` | `2dddf1f806ec2a048b1880eb8ddf4afa` |
| `Assets/SideQuest/LightingTools/LightProbes` | `09fb8e0c69ed94f4fb6aaea21101d931` |
| `Assets/SideQuest/LightingTools/Occlusion` | `5338eaa35f602f547b8a6b461efae0b0` |
| `Assets/SideQuest/LightingTools/ReflectionProbes` | `704c09ef582dd53468f940cdec2105c3` |
| `Assets/SideQuest/LightingTools/Bake/SideQuest.LightingTools.Bake.Editor.asmdef` | `856ab870238e983469cd501f58d180b2` |
| `Assets/SideQuest/LightingTools/Core/Analysis` | `6724de61b4dba8f4f909a1645270c08e` |
| `Assets/SideQuest/LightingTools/Core/Io` | `8278096db14b31b40ab5499a5d153e23` |
| `Assets/SideQuest/LightingTools/Core/Json` | `f52aa93529ddc594bb76dc8c90f892d0` |
| `Assets/SideQuest/LightingTools/Core/Model` | `db70f53343ad9244ea9c1a37b6f8ed93` |
| `Assets/SideQuest/LightingTools/Core/SideQuest.LightingTools.Core.Editor.asmdef` | `6a3b00b888f6f134ea607c20172dfffa` |
| `Assets/SideQuest/LightingTools/Core/SqLightingCore.cs` | `1686ca8e3f7eac843b572a029cef4f32` |
| `Assets/SideQuest/LightingTools/Core/Ui` | `2c2421feae840044fad78569fc7b022d` |
| `Assets/SideQuest/LightingTools/Core/Urp` | `96cde4b641e459b46b073b04107b4551` |
| `Assets/SideQuest/LightingTools/Core/Analysis/Histogram.cs` | `290b74e1525078744870f2e5963b0e38` |
| `Assets/SideQuest/LightingTools/Core/Analysis/IrradianceProbe.cs` | `7b3e9d2487deac04199de8ee6708cb61` |
| `Assets/SideQuest/LightingTools/Core/Analysis/MaterialFacts.cs` | `22c7af318750ad2489f1920676a5a620` |
| `Assets/SideQuest/LightingTools/Core/Analysis/OccupancyGrid.cs` | `9ab26bcc0b5b28c4293d0fd438adc48a` |
| `Assets/SideQuest/LightingTools/Core/Analysis/RendererFacts.cs` | `3abf327c0774f874c86a7a8a031805bb` |
| `Assets/SideQuest/LightingTools/Core/Analysis/SceneScale.cs` | `7f6850d7b9e58b1419b941a81c342ff8` |
| `Assets/SideQuest/LightingTools/Core/Analysis/SceneScanner.cs` | `dfdc4f4f7d83d66448132fd0b0d11e89` |
| `Assets/SideQuest/LightingTools/Core/Analysis/SpatialHash.cs` | `d9f39ea415b33b84287cd4b97ad3e21b` |
| `Assets/SideQuest/LightingTools/Core/Analysis/ZoneSegmenter.cs` | `5557c7aa224d53a47b3206819acc7fbd` |
| `Assets/SideQuest/LightingTools/Core/Io/PlanValidator.cs` | `812e84dafe55bab45bd9d15d3c7cd367` |
| `Assets/SideQuest/LightingTools/Core/Io/ReportPaths.cs` | `1cdacaf08b46e614fbb6e6e019a60908` |
| `Assets/SideQuest/LightingTools/Core/Io/ReportWriter.cs` | `9cfe2f99750662a41b119eb52a71bc78` |
| `Assets/SideQuest/LightingTools/Core/Io/SqToolContext.cs` | `564321d229f6b504a934c9a1637264ae` |
| `Assets/SideQuest/LightingTools/Core/Io/StatusFile.cs` | `d136b01b25d3e5745a7f1911d5574445` |
| `Assets/SideQuest/LightingTools/Core/Json/SqJsonParser.cs` | `0e80cad7832ee984191906808fc7413e` |
| `Assets/SideQuest/LightingTools/Core/Json/SqJsonValue.cs` | `38d6e80bf05f37b4d9c8fe0c2ab74221` |
| `Assets/SideQuest/LightingTools/Core/Json/SqJsonWriter.cs` | `582c730a2eacb8e41b06278eeb96f9b0` |
| `Assets/SideQuest/LightingTools/Core/Model/ApplyResult.cs` | `2a78d3c017511b84caa59ac757379835` |
| `Assets/SideQuest/LightingTools/Core/Model/DecisionPlan.cs` | `b14794cf39e1d4a4e96426fc8707e74e` |
| `Assets/SideQuest/LightingTools/Core/Model/SceneReport.cs` | `427eb435b002cab4298b82100423340d` |
| `Assets/SideQuest/LightingTools/Core/Model/SqFormat.cs` | `eef61f961ca01614b952d9f9395458f1` |
| `Assets/SideQuest/LightingTools/Core/Model/SqObjectId.cs` | `e660c1b66a234d645aabc93ef5a77ea8` |
| `Assets/SideQuest/LightingTools/Core/Model/SqProblem.cs` | `105aa51f8f962074288abad84da757ad` |
| `Assets/SideQuest/LightingTools/Core/Ui/SqLog.cs` | `ee919557c5aea854e9abc6342ccafc1b` |
| `Assets/SideQuest/LightingTools/Core/Ui/SqPreviewDraw.cs` | `00f3635c183d51845b2014be10a3dfed` |
| `Assets/SideQuest/LightingTools/Core/Ui/SqSettings.cs` | `822d0209581fdc54098c392f8ff134d4` |
| `Assets/SideQuest/LightingTools/Core/Ui/SqUndo.cs` | `bad45a61345793040835bda58f8608bb` |
| `Assets/SideQuest/LightingTools/Core/Urp/UrpFacts.cs` | `f3e0666f9737bbc4bbdacde34652a69f` |
| `Assets/SideQuest/LightingTools/Core/Urp/UrpGuards.cs` | `011deda0fb8428640854167e0e11abdf` |
| `Assets/SideQuest/LightingTools/LightProbes/AdaptiveSampler.cs` | `ac8760f4e878ce4448518a7b8e44ee1b` |
| `Assets/SideQuest/LightingTools/LightProbes/AgentSampler.cs` | `98fdcf2e2e753c544ba5ef9c566e46d4` |
| `Assets/SideQuest/LightingTools/LightProbes/ContributeGiAssigner.cs` | `f7c1cd8d0ed78f64c93aa45bb06c4f35` |
| `Assets/SideQuest/LightingTools/LightProbes/LightProbePlan.cs` | `5ee3c68595788584bbe57c610d32e066` |
| `Assets/SideQuest/LightingTools/LightProbes/LightProbeTool.cs` | `8059a0312d2d6f14ca88c1b009c16fb1` |
| `Assets/SideQuest/LightingTools/LightProbes/LightProbeWindow.cs` | `90949f6d0b55b4d4fbc0c3b16ac091f7` |
| `Assets/SideQuest/LightingTools/LightProbes/MeshVolumeSampler.cs` | `5ef4a91db37005f4fa8ba3854ec9666d` |
| `Assets/SideQuest/LightingTools/LightProbes/NavMeshSampler.cs` | `782eaf07d22459545b2f4255ad724e25` |
| `Assets/SideQuest/LightingTools/LightProbes/ProbeGroupWriter.cs` | `04e3d29c59042d549abd4d13b092a8c9` |
| `Assets/SideQuest/LightingTools/LightProbes/ProbePaintMode.cs` | `f141debf75677ee4c8c0f25d1d8c5e25` |
| `Assets/SideQuest/LightingTools/LightProbes/ProbeSampler.cs` | `9f806964bd8a64b47a32967afa1d3da4` |
| `Assets/SideQuest/LightingTools/LightProbes/SideQuest.LightingTools.LightProbes.Editor.asmdef` | `44a00cf7f59342e469bd6a1507c02423` |
| `Assets/SideQuest/LightingTools/Occlusion/SideQuest.LightingTools.Occlusion.Editor.asmdef` | `bc10bca39ad26194db4d2621715008d6` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbeApplier.cs` | `73a717cd9a90d134d83c03c126b4abf9` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbeClusterer.cs` | `6c46ddff94a1e4141bde757296462caa` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbePlacer.cs` | `3b65db3235c148b42bb2172261a42e85` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbePlan.cs` | `aefc88dfc281a554f818ef5087968fd3` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbeSpec.cs` | `ca8d28e1b0b90bb4b83e1cc89c6ea9d5` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbeTool.cs` | `a0b9a5a4b8bd2a34c9e6b57bd01ea9f5` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/ReflectionProbeWindow.cs` | `1e3276bb04378424bbc7e2229960005a` |
| `Assets/SideQuest/LightingTools/ReflectionProbes/SideQuest.LightingTools.ReflectionProbes.Editor.asmdef` | `e68d4403e260904428cf921725986204` |

64 entries.
