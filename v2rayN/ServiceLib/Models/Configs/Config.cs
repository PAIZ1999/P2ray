namespace ServiceLib.Models.Configs;

[Serializable]
public class Config
{
    #region property

    public string IndexId { get; set; }
    public string SubIndexId { get; set; }

    #endregion property

    #region other entities

    public CoreBasicItem CoreBasicItem { get; set; }
    public TunModeItem TunModeItem { get; set; }
    public KcpItem KcpItem { get; set; }
    public GrpcItem GrpcItem { get; set; }
    public RoutingBasicItem RoutingBasicItem { get; set; }
    public GUIItem GuiItem { get; set; }
    public MsgUIItem MsgUIItem { get; set; }
    public UIItem UiItem { get; set; }
    public ConstItem ConstItem { get; set; }
    public SpeedTestItem SpeedTestItem { get; set; }
    public Mux4RayItem Mux4RayItem { get; set; }
    public Mux4SboxItem Mux4SboxItem { get; set; }
    public HysteriaItem HysteriaItem { get; set; }
    public ClashUIItem ClashUIItem { get; set; }
    public SystemProxyItem SystemProxyItem { get; set; }
    public WebDavItem WebDavItem { get; set; }
    public CheckUpdateItem CheckUpdateItem { get; set; }
    public Fragment4RayItem? Fragment4RayItem { get; set; }
    public List<InItem> Inbound { get; set; }
    public List<KeyEventItem> GlobalHotkeys { get; set; }
    public List<CoreTypeItem> CoreTypeItem { get; set; }
    public SimpleDNSItem SimpleDNSItem { get; set; }
    public HappyEyeballs4RayItem HappyEyeballs4RayItem { get; set; }

    /// <summary>
    /// 已经跑过的一次性配置迁移的名字，逗号分隔。
    /// 修改默认值无法影响老用户的配置 —— 字段早就以 false 落盘了，
    /// 所以这类"改了默认值还要对老用户生效"的改动必须靠迁移补一次。
    /// </summary>
    public string AppliedMigrations { get; set; }

    #endregion other entities
}
