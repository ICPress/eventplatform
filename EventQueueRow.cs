public class EventQueueRow
{
    public uint event_id { get; set; }  = 0; 
    public string trigger_source_username { get; set; } = "";

    public string additional_data { get; set; }= "";

    public EventTriggerType type { get; set; }  = EventTriggerType.UNDEFINED;

    public bool claimed { get; set; }= false;
    public bool skipEvent { get; set; } = false;
    
}