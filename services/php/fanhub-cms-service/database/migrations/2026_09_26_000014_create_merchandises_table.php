<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        if (!Schema::hasTable('merchandises')) {
            Schema::create('merchandises', function (Blueprint $table) {
                $table->string('id', 64)->primary();
                $table->string('category_id', 64)->nullable()->index();
                $table->string('name', 255);
                $table->decimal('price', 19, 4)->default(0);
                $table->string('tag', 100)->nullable();
                $table->string('image_url', 500)->nullable();
                $table->text('description')->nullable();
                $table->integer('stock_quantity')->default(0);
                $table->integer('sold_quantity')->default(0);
                $table->string('status', 20)->default('Active');
                $table->timestamps();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('merchandises');
    }
};
