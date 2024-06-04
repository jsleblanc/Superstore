namespace OrderDownloader;

public class OrderHistoryItem
{
   public string id { get; set; }
   public decimal total { get; set; }
   public DateTimeOffset placed { get; set; }
   public string store { get; set; }
}

public class OrderHistory
{
   public int offlineOrdersCount { get; set; }
   public int onlineOrdersCount { get; set; }
   public OrderHistoryItem[] orderHistory { get; set; }
}