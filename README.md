# README

Login to https://www.realcanadiansuperstore.ca/ with the dev-console of your browser open. Look at network calls and look
for any that smell like API requests. Once you find one, extract the `Authorization` bearer token and the `x-apikey` headers.

Create a file called `.env` that looks like this and insert the values you grabbed:

```
AUTH_TOKEN=foo
API_KEY=bar
```

Save this `.env` file in the `OrderDownloader` project folder, then run this tool. It will output a sqlite database file 
to its working directory called `orders.sqlite`.

Copy this `orders.sqlite` file into the same directory as the executable of the `OrderAnalysis` tool and run that, it will
generate two Excel spreadsheet files. 

One is every product purchased, one purchase per row.

The other has been normalized so that there is a row per purchase per category of product, so that you can group by
product category (products will each have several categories).