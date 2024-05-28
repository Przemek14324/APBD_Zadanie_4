using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Data.SqlClient;

namespace WarehouseManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WarehouseController : ControllerBase
    {
        private readonly string _connectionString;

        public WarehouseController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost("addProduct")]
        public IActionResult AddProductToWarehouse([FromBody] ProductWarehouseRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                SqlTransaction transaction = connection.BeginTransaction();

                try
                {
                    // Check if product exists
                    SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM Product WHERE IdProduct = @IdProduct", connection, transaction);
                    command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                    int productCount = (int)command.ExecuteScalar();

                    if (productCount == 0)
                    {
                        return NotFound("Product not found.");
                    }

                    // Check if warehouse exists
                    command = new SqlCommand("SELECT COUNT(*) FROM Warehouse WHERE IdWarehouse = @IdWarehouse", connection, transaction);
                    command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                    int warehouseCount = (int)command.ExecuteScalar();

                    if (warehouseCount == 0)
                    {
                        return NotFound("Warehouse not found.");
                    }

                    // Check if order exists
                    command = new SqlCommand("SELECT IdOrder, Price FROM [Order] WHERE IdProduct = @IdProduct AND Amount = @Amount AND CreatedAt < @CreatedAt", connection, transaction);
                    command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                    command.Parameters.AddWithValue("@Amount", request.Amount);
                    command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);
                    SqlDataReader reader = command.ExecuteReader();

                    if (!reader.Read())
                    {
                        return NotFound("Order not found.");
                    }

                    int orderId = reader.GetInt32(0);
                    decimal price = reader.GetDecimal(1);
                    reader.Close();

                    // Check if order is fulfilled
                    command = new SqlCommand("SELECT COUNT(*) FROM Product_Warehouse WHERE IdOrder = @IdOrder", connection, transaction);
                    command.Parameters.AddWithValue("@IdOrder", orderId);
                    int productWarehouseCount = (int)command.ExecuteScalar();

                    if (productWarehouseCount > 0)
                    {
                        return BadRequest("Order already fulfilled.");
                    }

                    // Update Order FulfilledAt
                    command = new SqlCommand("UPDATE [Order] SET FulfilledAt = @FulfilledAt WHERE IdOrder = @IdOrder", connection, transaction);
                    command.Parameters.AddWithValue("@FulfilledAt", DateTime.Now);
                    command.Parameters.AddWithValue("@IdOrder", orderId);
                    command.ExecuteNonQuery();

                    // Insert into Product_Warehouse
                    command = new SqlCommand(
                        "INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) " +
                        "OUTPUT INSERTED.IdProductWarehouse " +
                        "VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Price, @CreatedAt)", connection, transaction);

                    command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                    command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                    command.Parameters.AddWithValue("@IdOrder", orderId);
                    command.Parameters.AddWithValue("@Amount", request.Amount);
                    command.Parameters.AddWithValue("@Price", price * request.Amount);
                    command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                    int productWarehouseId = (int)command.ExecuteScalar();

                    transaction.Commit();

                    return Ok(new { IdProductWarehouse = productWarehouseId });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return StatusCode(500, $"Internal server error: {ex.Message}");
                }
            }
        }

        [HttpPost("addProductWithProc")]
        public IActionResult AddProductToWarehouseWithProc([FromBody] ProductWarehouseRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                SqlCommand command = new SqlCommand("AddProductToWarehouse", connection)
                {
                    CommandType = System.Data.CommandType.StoredProcedure
                };

                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

                SqlParameter outputId = new SqlParameter("@NewId", System.Data.SqlDbType.Int)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                command.Parameters.Add(outputId);

                try
                {
                    command.ExecuteNonQuery();
                    int productWarehouseId = (int)outputId.Value;

                    return Ok(new { IdProductWarehouse = productWarehouseId });
                }
                catch (SqlException ex)
                {
                    return StatusCode(500, $"Stored procedure error: {ex.Message}");
                }
            }
        }
    }

    public class ProductWarehouseRequest
    {
        public int IdProduct { get; set; }
        public int IdWarehouse { get; set; }
        public int Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
