// For more information see https://aka.ms/fsharp-console-apps

open System
open System.Data
open System.Data.SQLite
open System.Text.Json
open FsExcel

type ProductCode = string
type OrderNumber = string

type Product = {
    articleNumber: string
    baseUnit: string
    description: string
    eyebrow: string
    id: ProductCode
    price: decimal option
    brand: string
    productName: string
    sellingType: string 
    sellingUnit: string    
}

type ProductDetails = {
    code: ProductCode
    name: string
    brand: string
    description: string
    categories: string list
}

type OrderItem = {
   product: Product
   quantity: int
   totalPrice: decimal 
   unitPrice: decimal
   weight: decimal option
}

type OrderDetails = {
    orderNumber: OrderNumber
    subTotal: decimal
    totalDiscounts: decimal
    totalPrice: decimal
    totalPriceWithTax: decimal
    totalTax: decimal
    entries: OrderItem list
}
    
type Order = {
    created: DateTimeOffset
    orderDetails: OrderDetails
}

type AggregatedOrderProduct = {
    created: DateTimeOffset
    orderNumber: OrderNumber
    productId: ProductCode
    productPrice: decimal option
    productName: string
    productBrand: string
    productDescription: string
    productBaseUnit: string
    productCategory: string
    totalPrice: decimal
    quantity: int
    unitPrice: decimal
    weight: decimal option
}


let getDbConnection dbFile =
    let builder = SQLiteConnectionStringBuilder()
    builder.DataSource <- dbFile
    builder.ReadOnly <- true
    let c = new SQLiteConnection(builder.ConnectionString)
    c.OpenAndReturn()
    
let getOrdersAsStrings (connection:SQLiteConnection) =
    let cmd = connection.CreateCommand()
    cmd.CommandText <- "SELECT orderBody FROM orders;"
    cmd.CommandType <- CommandType.Text
    use reader = cmd.ExecuteReader()
    [
        while reader.Read() do
            yield reader.GetString(reader.GetOrdinal("orderBody"))
    ]

let getProductDetails (connection:SQLiteConnection) =
    let cmd = connection.CreateCommand()
    cmd.CommandText <- """
        SELECT
            productCode,
            json_extract(p.productBody, '$.name') as name,
            coalesce(json_extract(p.productBody, '$.brand'), "") as brand,
            coalesce(json_extract(p.productBody, '$.description'), "") as description,
            json_extract(value, '$.categoryCode') as categoryCode,
            json_extract(value, '$.name') as categoryName
        FROM products p, json_each(p.productBody, '$.breadcrumbs');
        """
    cmd.CommandType <- CommandType.Text
    use reader = cmd.ExecuteReader()
    [
        while reader.Read() do
            let row = {|
                        productCode = reader.GetString(reader.GetOrdinal("productCode"))
                        name = reader.GetString(reader.GetOrdinal("name"))
                        brand = reader.GetString(reader.GetOrdinal("brand"))
                        description = reader.GetString(reader.GetOrdinal("description"))
                        categoryCode = reader.GetString(reader.GetOrdinal("categoryCode"))
                        categoryName = reader.GetString(reader.GetOrdinal("categoryName"))
                       |}
            yield row
    ]
    |> List.ofSeq
    |> List.groupBy (_.productCode)
    |> List.map (fun (productCode, products) -> {
        code = ProductCode productCode
        name = products |> List.map (_.name) |> List.head
        brand = products |> List.map (_.brand) |> List.head
        description = products |> List.map (_.description) |> List.head
        categories = products |> List.map (_.categoryName) 
        })
    |> List.map (fun p -> (p.code, p))
    |> Map.ofList 

let foldOrderNormalizedCategories m order =
    seq {
        for entry in order.orderDetails.entries do
            match Map.tryFind entry.product.id m with
            | Some product ->
                for category in product.categories do
                    yield {
                        created = order.created
                        orderNumber = order.orderDetails.orderNumber
                        productId = entry.product.id
                        productPrice = entry.product.price
                        productName = entry.product.productName
                        productBrand = entry.product.brand
                        productDescription = entry.product.description
                        productBaseUnit = entry.product.baseUnit
                        productCategory = category 
                        totalPrice = entry.totalPrice
                        quantity = entry.quantity
                        unitPrice = entry.unitPrice
                        weight = entry.weight 
                    }                     
            | None -> 
                yield {
                    created = order.created
                    orderNumber = order.orderDetails.orderNumber
                    productId = entry.product.id
                    productPrice = entry.product.price
                    productName = entry.product.productName
                    productBrand = entry.product.brand 
                    productDescription = entry.product.description
                    productBaseUnit = entry.product.baseUnit
                    productCategory = "unknown" 
                    totalPrice = entry.totalPrice
                    quantity = entry.quantity
                    unitPrice = entry.unitPrice
                    weight = entry.weight 
                }            
    } |> List.ofSeq

let foldOrder order =
    seq {
        for entry in order.orderDetails.entries do
            yield {
                created = order.created
                orderNumber = order.orderDetails.orderNumber
                productId = entry.product.id
                productPrice = entry.product.price
                productName = entry.product.productName
                productBrand = entry.product.brand 
                productDescription = entry.product.description
                productBaseUnit = entry.product.baseUnit
                productCategory = ""
                totalPrice = entry.totalPrice
                quantity = entry.quantity
                unitPrice = entry.unitPrice
                weight = entry.weight 
            }            
    } |> List.ofSeq

let writeExcel fileName items =
    let decOptToFloatOpt x =
        match x with
        | Some d -> float d
        | None -> 0
    [
        Cell [ String "Date" ]
        Cell [ String "Year" ]
        Cell [ String "Order Number" ]        
        Cell [ String "Product ID" ]
        Cell [ String "Product Name" ]
        Cell [ String "Product Brand" ]
        Cell [ String "Description" ]
        Cell [ String "Category" ]
        Cell [ String "Price" ]
        Cell [ String "Base Unit" ]
        Cell [ String "Quantity" ]
        Cell [ String "Total Price" ]
        Cell [ String "Unit Price" ]
        Cell [ String "Weight" ]
        Go NewRow
        
        for i in items do
            Cell [
                DateTime i.created.LocalDateTime
            ]
            Cell [
                Integer i.created.LocalDateTime.Year
                FormatCode "0"
            ]
            Cell [
                String i.orderNumber            
            ]
            Cell [
                String i.productId
            ]
            Cell [
                String i.productName
            ]
            Cell [
                String i.productBrand
            ]
            Cell [
                String i.productDescription
            ]
            Cell [
                String i.productCategory
            ]
            Cell [
                Float (decOptToFloatOpt i.productPrice)
                FormatCode "$#,##.##"
            ]
            Cell [
                String i.productBaseUnit
            ]
            Cell [
                Integer i.quantity
            ]
            Cell [
                Float (float i.totalPrice)
                FormatCode "$#,##.##"
            ]
            Cell [
                Float (float i.unitPrice)
                FormatCode "$#,##.##"
            ]
            Cell [
                Float (decOptToFloatOpt i.weight)
            ]
            Go NewRow 
    ] @ [
        AutoFit All
        FreezePanes TopRow
        AutoFilter [ EnableOnly RangeUsed ]
    ]
    |> Render.AsFile(fileName)
    
    
let con = getDbConnection "orders.sqlite"
let productsMap = getProductDetails con

getOrdersAsStrings con
|> List.map JsonSerializer.Deserialize<Order>
|> List.map (foldOrderNormalizedCategories productsMap)
|> List.concat
|> writeExcel "orderHistoryNormalizedCategories.xlsx"

getOrdersAsStrings con
|> List.map JsonSerializer.Deserialize<Order>
|> List.map foldOrder
|> List.concat
|> writeExcel "orderHistory.xlsx"

