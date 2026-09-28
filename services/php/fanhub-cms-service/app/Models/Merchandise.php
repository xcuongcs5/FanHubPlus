<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Str;

class Merchandise extends Model
{
    use HasFactory;

    protected $table = 'merchandises';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'category_id',
        'name',
        'price',
        'tag',
        'image_url',
        'description',
        'stock_quantity',
        'sold_quantity',
        'status',
    ];

    protected static function booted(): void
    {
        static::creating(function (Merchandise $item) {
            if (empty($item->id)) {
                $item->id = 'mrc_' . Str::lower(Str::random(12));
            }
            if (empty($item->status)) {
                $item->status = 'Active';
            }
        });
    }

    /**
     * Danh mục của vật phẩm
     */
    public function category(): BelongsTo
    {
        return $this->belongsTo(Category::class, 'category_id');
    }
}
