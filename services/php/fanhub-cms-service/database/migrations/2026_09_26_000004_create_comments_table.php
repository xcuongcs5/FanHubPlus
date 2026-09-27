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
        Schema::create('comments', function (Blueprint $table) {
            $table->string('id', 64)->primary();
            $table->string('target_id', 64)->index();
            $table->string('user_id', 64)->index();
            $table->string('parent_id', 64)->nullable()->index();
            $table->string('title')->nullable();
            $table->text('body');
            $table->string('status', 50)->default('active');
            $table->timestamps();

            $table->foreign('target_id')
                ->references('id')
                ->on('contents')
                ->onDelete('cascade');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('comments');
    }
};
