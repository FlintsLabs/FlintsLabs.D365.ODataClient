namespace FlintsLabs.D365.ODataClient.Enums;
 
 /// <summary>
 /// Predefined names for D365 Service instances to avoid typos.
 /// </summary>
 public enum D365ServiceScope
 {
     /// <summary>
     /// Default service instance ("Default")
     /// </summary>
     Default,
 
     /// <summary>
     /// Cloud D365 instance ("Cloud")
     /// </summary>
     Cloud,
 
     /// <summary>
     /// On-Premise D365 instance ("OnPrem")
     /// </summary>
     OnPrem,
 
     /// <summary>
     /// Microsoft Dataverse / CRM instance ("Dataverse")
     /// </summary>
     Dataverse
 }
