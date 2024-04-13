using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderDelightLibrary.Services;
using OrderDelightLibrary.Shared.DTOs;
using OrderDelightLibrary.Shared.Models;
using OrderDelightLibrary.Shared.Services;
using OrderDelightLibrary.Shared.Utilities;

namespace OrderDelightStoreApp.Services
{
    public class StoreAppService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private string ImageStorageAccountUrl { get; set; }
        public Store? CurrentStore { get; set; }
        public List<FoodItem>? FoodItems { get; set; }
        public List<Menu>? Menus { get; set; }
        public StoreAppService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-functions-key", "xxx"); //temporarily used, change to Oauth later
            AddCurrentUserAndStoreHeaders("101-1", "757a7d94-473b-40ca-bc2f-b127333c1536");
            ImageStorageAccountUrl = "https://scantopay.blob.core.windows.net"; //_configuration["storage-account-url"] ?? "";
        }

        public void AddCurrentUserAndStoreHeaders(string storeId, string userId)
        {
            UpdateHeader("store-id", storeId);
            UpdateHeader("user-id", userId);
        }
        public void UpdateHeader(string headerName, string headerValue)
        {
            // Remove the old header if it exists
            _httpClient.DefaultRequestHeaders.Remove(headerName);
            // Add the new header
            _httpClient.DefaultRequestHeaders.Add(headerName, headerValue);
        }
        public async Task LoadStoreAsync(string storeId)
        {
            var requestUrl = $"api/store?storeid={storeId}";
            CurrentStore = await GetAsync<Store>(requestUrl);
        }
        #region language related
        public event Action OnLanguageChanged;
        public event Action OnSupportedLanguagesChanged;

        private string? _currentSelectedLanguage;
        public string CurrentSelectedLanguage
        {
            get => _currentSelectedLanguage ?? "en";
            set
            {
                _currentSelectedLanguage = value;
                OnLanguageChanged?.Invoke();
            }
        }
        public string ResolveLanguage(Dictionary<string, string>? dic)
        {
            return ResolveLanguage(dic, CurrentSelectedLanguage);
        }
        public string ResolveLanguage(Dictionary<string, string>? dic, string lang)
        {
            if (dic == null || !dic.Any()) return "";

            if (dic.TryGetValue(lang, out var content))
                return content;
            if (dic.TryGetValue("en", out var content2))
                return content2;
            return dic.FirstOrDefault().Value;
        }

        public string ResolveLanguageDescription(string lang)
        {
            if (SystemSupportedLanguages.TryGetValue(lang, out var content))
                return content;

            return lang;
        }

        public Dictionary<string, string> SystemSupportedLanguages = new Dictionary<string, string>();

        public void LoadSystemSupportedLanguages()
        {
            SystemSupportedLanguages.Add("en", "English");
            SystemSupportedLanguages.Add("zh", "简体中文");
            SystemSupportedLanguages.Add("cn", "繁体中文");
            SystemSupportedLanguages.Add("fr", "Français"); // Corrected French
            SystemSupportedLanguages.Add("es", "Español"); // Corrected Spanish
        }
        public List<Language> GetSystemSupportedLanguages()
        {
            return SystemSupportedLanguages.Select(kv => new Language { Code = kv.Key, Name = kv.Value }).ToList();
        }

        #endregion
        //public async Task<List<Menu>> GetActiveMenusAsync()
        //{
        //    var url = $"/products";
        //    var response = await _httpClient.GetAsync(url);
        //    response.EnsureSuccessStatusCode();
        //    var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        //    return products;
        //}

        //public async Task<Product> GetProductAsync(int id)
        //{
        //    var response = await _httpClient.GetAsync($"{_configuration["ApiUrl"]}/products/{id}");
        //    response.EnsureSuccessStatusCode();
        //    var product = await response.Content.ReadFromJsonAsync<Product>();
        //    return product;
        //}
        public string GetFoodItemImageUrl(FoodItem foodItem)
        {
            var mainStoreId = StoreService.GetMainStoreId(CurrentStore?.StoreId).ToString();
            return $"{ImageStorageAccountUrl}/store-images/{mainStoreId}/food-items/store-{mainStoreId}-fooditem-{foodItem.id}.jpg?v={foodItem.ImageVersion}";
        }
        public string GetStoreLogoImageUrl()
        {
            return $"{ImageStorageAccountUrl}/store-images/{CurrentStore?.StoreId}/store-{CurrentStore?.StoreId}-logo.jpg?v={CurrentStore.LogoImageVersion}";
        }
        public string GetStoreHeroImageUrl()
        {
            return $"{ImageStorageAccountUrl}/store-images/{CurrentStore?.StoreId}/store-{CurrentStore?.StoreId}-hero.jpg?v={CurrentStore.HeroImageVersion}";
        }

        public ImageSize GetLargeFoodItemImageSize()
        {
            //return CommonUtilities.GetImageSize(_configuration["ImageSize-FoodItem-Large"]);
            return CommonUtilities.GetImageSize("190,190");
        }
        public ImageSize GetSmallFoodItemImageSize()
        {
            // return CommonUtilities.GetImageSize(_configuration["ImageSize-FoodItem-Small"]);
            return CommonUtilities.GetImageSize("55,55");
        }
        public ImageSize GetLogoImageSize()
        {
            //return CommonUtilities.GetImageSize(_configuration["ImageSize-Logo"]);
            return CommonUtilities.GetImageSize("120,120");
        }
        public ImageSize GetHeroImageSize()
        {
            // return CommonUtilities.GetImageSize(_configuration["ImageSize-Hero"]);
            return CommonUtilities.GetImageSize("548,200");
        }
        public async Task<List<OptInLookup>?> GetOptInsAsync(string storeId)
        {
            var requestUrl = "api/store/optIn";
            var response = await GetAsync<GenericReturnDto>(requestUrl);
            if (response.IsSuccess)
            {
                var option = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var optIns = JsonSerializer.Deserialize<List<OptInLookup>>(response.Data.ToString(), option);
                return optIns;
            }

            return null;
        }
        public async Task<List<Menu>> GetFilteredMenusAsync()
        {
            //Use this for a full list of menus for any type of store

            var filteredMenuItems = new List<Menu>();
            if (IsIndependentStore() || IsMainChainedStore())
            {
                filteredMenuItems = await GetLocalStoreMenusAsync() ?? new List<Menu>();
            }
            if (IsLocalChainedStore())
                filteredMenuItems = await GetCombinedMenusAsync("All");

            return filteredMenuItems;
        }

        public async Task<List<Menu>> GetCombinedMenusAsync(string selectedMenuType)
        {
            //For Local Chained Store

            if (CurrentStore == null) return new List<Menu>();
            var currentOptedInMenus = await GetOptedInMenusAsync();

            var allMenusFromMainStore = await GetMainStoreMenusAsync();
            if (allMenusFromMainStore == null)
                allMenusFromMainStore = new List<Menu>();

            var menusFromMainStore = new List<Menu>();
            var menusFromLocalStore = new List<Menu>();
            if (IsLocalChainedStore())
            {
                menusFromMainStore = allMenusFromMainStore.Where(m => m.IsEnabled && currentOptedInMenus.Contains(m.id)).ToList();
                menusFromLocalStore = await GetLocalStoreMenusAsync() ?? new List<Menu>();
            }
            else
                menusFromMainStore = allMenusFromMainStore;


            if (selectedMenuType == "EnabledOnly")
            {
                var enabledFromMainStore = menusFromMainStore.Where(m => m.IsEnabled).ToList();
                var enabledFromLocalStore = menusFromLocalStore.Where(m => m.IsEnabled).ToList();
                return enabledFromMainStore.Concat(enabledFromLocalStore).ToList();
            }

            if (selectedMenuType == "LocalStoreOnly")
            {
                return menusFromLocalStore;
            }

            if (selectedMenuType == "MainStoreOnly")
            {
                return allMenusFromMainStore;
            }

            if (selectedMenuType == "All")
            {
                return allMenusFromMainStore.Concat(menusFromLocalStore).ToList();
            }
            return new List<Menu>();
        }

        async Task<List<string>> GetOptedInMenusAsync()
        {
            if (CurrentStore == null) return new List<string>();
            var allOptIns = await GetOptInsAsync(CurrentStore.StoreId) ?? new List<OptInLookup>();
            return allOptIns.Where(o => o.OptInType == "Menu").Select(o => o.ExternalId).ToList();
        }
        public bool IsLocalChainedStore()
        {
            if (CurrentStore == null) return false;
            if (CurrentStore.IsChainStore && CurrentStore.StoreId.Contains("-"))
                return true;
            return false;
        }
        public bool IsMainChainedStore()
        {
            if (CurrentStore == null) return false;
            if (CurrentStore.IsChainStore && !CurrentStore.StoreId.Contains("-"))
                return true;
            return false;
        }
        public bool IsMainStore(Store? store)
        {
            return store != null && store.IsChainStore && !store.StoreId.Contains("-");
        }
        public bool IsIndependentStore()
        {
            if (CurrentStore == null) return false;
            if (!CurrentStore.IsChainStore)
                return true;
            return false;
        }

        public async Task<List<Menu>?> GetMainStoreMenusAsync()
        {
            if (CurrentStore == null) return new List<Menu>();
            var requestUrl = "api/store/menus?storeId=" + StoreService.GetMainStoreId(CurrentStore.StoreId);
            return await GetAsync<List<Menu>>(requestUrl);
        }
        public async Task<List<Menu>?> GetLocalStoreMenusAsync()
        {
            if (CurrentStore == null) return new List<Menu>();
            var requestUrl = "api/store/menus?storeId=" + CurrentStore.StoreId;
            return await GetAsync<List<Menu>>(requestUrl);
        }

        public async Task<List<FoodItem>?> GetFoodItemsAsync()
        {

            var requestUrl = "api/store/food-items";
            var responseString = await _httpClient.GetStringAsync(requestUrl);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            FoodItems = JsonSerializer.Deserialize<List<FoodItem>>(responseString, options);
            return FoodItems;
        }

        //public async Task<List<Order>> GetOrdersAsync()
        //{
        //    var response = await _httpClient.GetAsync($"{_configuration["ApiUrl"]}/orders");
        //    response.EnsureSuccessStatusCode();
        //    var orders = await response.Content.ReadFromJsonAsync<List<Order>>();
        //    return orders;
        //}

        //public async Task<Order> GetOrderAsync(int id)
        //{
        //    var response = await _httpClient.GetAsync($"{_configuration["ApiUrl"]}/orders/{id}");
        //    response.EnsureSuccessStatusCode();
        //    var order = await response.Content.ReadFromJsonAsync<Order>();
        //    return order;
        //}

        //public async Task<Order> CreateOrderAsync(Order order)
        //{
        //    var response = await _httpClient.PostAsJsonAsync($"{_configuration["ApiUrl"]}/orders", order);
        //    response.EnsureSuccessStatusCode();
        //    var createdOrder = await response.Content.ReadFromJsonAsync<Order>();
        //    return createdOrder;
        //}

        //public async Task<Order> UpdateOrderAsync(int id, Order order)
        //{
        //    var response = await _httpClient.PutAsJsonAsync($"{_configuration["ApiUrl"]}/orders/{id}", order);
        //    response.EnsureSuccessStatusCode();
        //    var updatedOrder = await response.Content.ReadFromJsonAsync<Order>();
        //    return updatedOrder;
        //}

        //public async Task DeleteOrderAsync(int id)
        //{
        //    var response = await _httpClient.DeleteAsync($"{_configuration["ApiUrl"]}/orders/{id}");
        //    response.EnsureSuccessStatusCode();
        //}

        #region Generic API Calls
        // Enhanced Generic GET method
        // Method to log and throw exception
        private void LogAndThrowException(string message, Exception ex = null)
        {
            Console.WriteLine(message);
            if (ex != null)
            {
                throw new Exception(message, ex);
            }
            else
            {
                throw new Exception(message);
            }
        }
        // Enhanced Generic GET method
        private async Task<T?> GetAsync<T>(string requestUrl)
        {
            try
            {
                var response = await _httpClient.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<T>();
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    LogAndThrowException(response.ToString());
                }
                else
                {
                    var errorMessage = $"Error in GET request to '{requestUrl}': {response.StatusCode}";
                    LogAndThrowException(errorMessage);
                }

                // Handle error response
                var errorResponse = await response.Content.ReadFromJsonAsync<ReturnStatus>();
                if (errorResponse != null)
                {
                    var errorMessage = $"Error in GET request to '{requestUrl}': {errorResponse.ErrorMessage}, RequestId: {errorResponse.RequestId}, ErrorCode: {errorResponse.ErrorCode}";
                    LogAndThrowException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                LogAndThrowException($"Exception in GET request to '{requestUrl}': {ex.Message}", ex);
            }
            return default!;
        }

        // Enhanced Generic POST method
        private async Task<bool> PostAsync<T>(string requestUrl, T content)
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
                var jsonContent = JsonSerializer.Serialize(content, jsonOptions);
                Console.WriteLine($"Sending POST Request to {requestUrl} with content: {jsonContent}");

                using var response = await _httpClient.PostAsJsonAsync(requestUrl, content, jsonOptions);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Response: {responseBody}");
                    return true;
                }

                // Handle error response
                var errorMessage = $"POST Request to {requestUrl} failed with status code: {response.StatusCode} and content: {responseBody}";
                Console.WriteLine(errorMessage);
                LogAndThrowException(errorMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in POST request to '{requestUrl}': {ex.Message}");
                LogAndThrowException($"Exception in POST request to '{requestUrl}': {ex.Message}", ex);
            }
            return false;
        }


        // Enhanced Generic PUT method
        private async Task<bool> PutAsync<T>(string requestUrl, T content)
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
                var jsonContent = JsonSerializer.Serialize(content, jsonOptions);
                Console.WriteLine($"Sending PUT Request to {requestUrl} with content: {jsonContent}");

                using var response = await _httpClient.PutAsJsonAsync(requestUrl, content, jsonOptions);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Response: {responseBody}");
                    return true;
                }

                // Handle error response
                var errorMessage = $"PUT Request to {requestUrl} failed with status code: {response.StatusCode} and content: {responseBody}";
                Console.WriteLine(errorMessage);
                LogAndThrowException(errorMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in PUT request to '{requestUrl}': {ex.Message}");
                LogAndThrowException($"Exception in PUT request to '{requestUrl}': {ex.Message}", ex);
            }
            return false;
        }

        // Enhanced Generic DELETE method
        private async Task<bool> DeleteAsync(string requestUrl)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                // Handle error response
                var errorResponse = await response.Content.ReadFromJsonAsync<ReturnStatus>();
                if (errorResponse != null)
                {
                    var errorMessage = $"DELETE request to '{requestUrl}' failed: {errorResponse.ErrorMessage}, RequestId: {errorResponse.RequestId}, ErrorCode: {errorResponse.ErrorCode}";
                    LogAndThrowException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                LogAndThrowException($"Exception in DELETE request to '{requestUrl}': {ex.Message}", ex);
            }
            return false;
        }


        #endregion


    }
}
