/* Fetch prices and update the form */
fetch("http://localhost:2058/config")
  .then(r => r.json())
  .then(({basicPrice, proPrice}) => {
    const basicPriceInput = document.querySelector('#basicPrice');
    basicPriceInput.value = basicPrice;
    const proPriceInput = document.querySelector('#proPrice');
    proPriceInput.value = proPrice;
  })
