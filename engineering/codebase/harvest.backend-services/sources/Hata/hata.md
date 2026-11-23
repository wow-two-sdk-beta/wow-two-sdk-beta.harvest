# HATA

The HATA adapter reads [long-term apartment rentals in Tashkent](https://hata.uz/listings/rent/flats/tashkent). It follows the next page advertised in the catalog and saves each listing under the numeric ad ID in its public URL. Register `hata.uz` as a site and choose the Tashkent apartment rentals section. Requests use the source's robots policy and a minimum five-second interval.

The reader uses the listing's structured facts, full displayed description, characteristics and image URLs. It verifies the rental category and city before saving a detail page. HATA's displayed update date stays out of the post publication timestamp. The detail reader leaves account roles unknown when the publisher provides no role evidence.
