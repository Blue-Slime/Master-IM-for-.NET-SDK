using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class UpdateNotification
{
    public string Type { get; set; } = string.Empty;
    public List<string> PageIds { get; set; } = new();
}
